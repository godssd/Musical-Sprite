using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 静默期预热（2026-09-25 卡顿优化）：音乐起播帧第一批音符集中生成时会一次性付出
/// 「Resources.Load 纹理（磁盘 IO + 解压）+ 静态网格构建 + 材质创建 + shader 变体编译（编辑器大头）
/// + 纹理 GPU 上传」的首开成本，造成明显卡顿（场景 leadTime=4，起播帧即倾泻前 4 秒的音符）。
///
/// 本工具在开场动画 + 保底静默期间逐帧消化这些成本：每帧只做一小块，预热自身不产生可见卡顿；
/// 临时渲染物体 alpha=0，正常态视觉零变化。整个文件可整体删除回退（删掉 GameManager 里的
/// StartCoroutine(NotePrewarmer.Run()) 一行即可）。幂等：整场游戏只跑一次，RestartGame 重复调用安全。
/// </summary>
public static class NotePrewarmer
{
    private static bool _done;

    /// <summary>由 GameManager.Start 启动（需 MonoBehaviour 作协程宿主）。</summary>
    public static IEnumerator Run()
    {
        if (_done) yield break;
        _done = true;

        // 1. 强制加载精灵库：SO 及其全部引用纹理进内存（最大的一笔：IO + 解压）
        NoteSpriteLibrary lib = NoteSpriteLibrary.Instance;
        yield return null;

        // 1.5 预加载 BGM 音频数据：工程内 mp3 导入设置为 preloadAudioData=0（Decompress On Load），
        //     音频数据要等到 PlayScheduled 真正出声那一刻才加载/解压——主线程冻结一帧，
        //     正是"音乐起播瞬间小卡顿"的元凶。静默期主动 LoadAudioData 把这笔成本挪进静默期消化。
        //     loadState 轮询逐帧让路，不阻塞渲染；RestartGame 时 clip 已加载（loadState=Loaded）直接跳过。
        Conductor cond = UnityEngine.Object.FindObjectOfType<Conductor>();
        if (cond != null && cond.musicSource != null && cond.musicSource.clip != null
            && cond.musicSource.clip.loadState != AudioDataLoadState.Loaded)
        {
            cond.musicSource.clip.LoadAudioData();
            // 最多等 5 秒，防止异常文件卡死协程
            float deadline = Time.realtimeSinceStartup + 5f;
            while (cond.musicSource.clip.loadState == AudioDataLoadState.Loading
                   && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        // 2. 构建全部静态音符网格（quad / 圆角平板 / 圆柱）
        NoteMover.PrewarmMeshes();
        yield return null;

        // 3. 收集全部纹理（HashSet 去重：数字等精灵可能共享图集）
        var textures = new HashSet<Texture2D>();
        if (lib != null)
        {
            Collect(lib.tap, textures);
            Collect(lib.tapSelect, textures);
            Collect(lib.smallTap, textures);
            Collect(lib.smallTapSelect, textures);
            Collect(lib.wide, textures);
            Collect(lib.wideSelect, textures);
            Collect(lib.repeat, textures);
            Collect(lib.repeatSelect, textures);
            if (lib.repeatDigits != null) foreach (var s in lib.repeatDigits) Collect(s, textures);
            if (lib.repeatDigitsSelect != null) foreach (var s in lib.repeatDigitsSelect) Collect(s, textures);
            Collect(lib.slideLink, textures);
            Collect(lib.slideLinkSelect, textures);
            Collect(lib.slideJudgment, textures);
            Collect(lib.slideJudgmentSelect, textures);
            Collect(lib.beam, textures);
            Collect(lib.sparkle, textures);
        }

        // 4. 每种贴图建一个 alpha=0 采样材质，挂到相机前的临时 quad 上真渲染 2 帧：
        //    触发 shader 变体编译与纹理 GPU 上传，随后销毁。对玩家不可见。
        Camera cam = Camera.main;
        if (cam != null && textures.Count > 0)
        {
            var warmRoot = new GameObject("NotePrewarmRuntime (alpha0, auto-destroy)");
            int i = 0;
            foreach (var tex in textures)
            {
                if (tex == null) continue;
                Material mat = NoteMover.BuildPrewarmMaterial(tex);
                if (mat == null) continue;

                var go = new GameObject("pw" + i++);
                go.transform.SetParent(warmRoot.transform, false);
                // 相机本地空间排布：正前方 2 单位，网格铺开避免互相 z-fight；旋转 90° 让横放 quad 面向相机
                float col = i % 5, row = i / 5;
                go.transform.localPosition = new Vector3((col - 2f) * 0.7f, -(row - 0.5f) * 0.7f, 2f);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                go.transform.localScale = Vector3.one * 0.4f;

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = NoteMover.FlatQuadMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.material = mat;   // 实例化 alpha=0 材质，渲染后随 warmRoot 销毁
            }

            yield return null;
            yield return null;   // 两帧确保真的渲染过（变体编译 + 纹理上传都发生在这两帧内）
            Object.Destroy(warmRoot);
        }
    }

    private static void Collect(Sprite s, HashSet<Texture2D> set)
    {
        if (s != null && s.texture != null) set.Add(s.texture);
    }
}
