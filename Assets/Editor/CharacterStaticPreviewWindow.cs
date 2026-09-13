#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Spine.Unity;

/// <summary>
/// 角色静帧预览工具（不进 Play 调位置 / 大小）。
///
/// 菜单：Tools → Musical-Sprite → 角色静帧预览
///
/// 用途（用户需求 @2026-09-13）：
///   导入/替换 Spine 角色后，不进入运行模式，就能在 Scene 视图里看到角色摆出指定动画的静帧，
///   并用 Unity 自带的移动 / 缩放工具调整它的位置与大小，确认摆在战斗场景里对不对。
///
/// 实现要点：
///   1) 复用工程既有范式——
///      - ArtSizePreviewWindow 的「场景里生成带前缀标签的临时对象 + 一键清除 + MarkSceneDirty」；
///      - ShitSpineSetup 验证过的「编辑器下用 SkeletonAnimation.AnimationName + loop 驱动 Spine
///        （Spine 组件标了 [ExecuteAlways]，Scene 视图会自动播放 loop 动画）」。
///   2) 命名解析与战斗中 CharacterAnimator 完全一致：完整动画名 = CharacterDataSO.animationPrefix + '_' + 槽位后缀。
///      本窗口镜像一份「状态→后缀」表（StateSuffix，必须与 CharacterAnimator.SlotTable 保持同步），
///      既用来摆姿势，也用来生成「动画可用性报告」——直接验证前缀解析对不对、Spine 里动画齐不齐。
///   3) 纯 Editor、新增单文件，不碰任何战斗运行时脚本（CharacterAnimator / CharacterCubeMarker 等）。
///
/// 注意：生成的临时对象带 [CharPreview] 前缀，关闭窗口或点「清除预览」即删；提交前务必清掉。
/// </summary>
public class CharacterStaticPreviewWindow : EditorWindow
{
    private const string Tag = "[CharPreview]";

    // —— 镜像 CharacterAnimator.SlotTable 的「状态→(后缀, 是否 loop)」。仅用于预览摆姿 + 可用性报告，不触碰战斗文件。改 SlotTable 时必须同步这里。 ——
    private static readonly Dictionary<CharacterAnimator.CharacterAnimationState, (string suffix, bool loop)> StateSuffix =
        new Dictionary<CharacterAnimator.CharacterAnimationState, (string suffix, bool loop)>
    {
        { CharacterAnimator.CharacterAnimationState.Opening,        ("01_Opening",     false) },
        { CharacterAnimator.CharacterAnimationState.PlayNormal,     ("02_Play_Normal",  true)  },
        { CharacterAnimator.CharacterAnimationState.PlayFever,      ("03_Play_Fever",   true)  },
        { CharacterAnimator.CharacterAnimationState.PlaySuperFever, ("03_Play_Fever",   true)  }, // 暂复用过热资源
        { CharacterAnimator.CharacterAnimationState.SkillLoop,      ("13_Skill_Loop",   true)  },
        { CharacterAnimator.CharacterAnimationState.Victory,        ("16_Victory01",    true)  },
        { CharacterAnimator.CharacterAnimationState.Fail,           ("17_Fail",         true)  },
        { CharacterAnimator.CharacterAnimationState.Special,        ("05_Special",      false) },
        { CharacterAnimator.CharacterAnimationState.TargetNormal,   ("06_Target_Normal", false) },
        { CharacterAnimator.CharacterAnimationState.TargetFever,    ("07_Target_Fever",  false) },
        { CharacterAnimator.CharacterAnimationState.Hit,            ("08_Hit",          false) },
        { CharacterAnimator.CharacterAnimationState.Dizziness,      ("09_Dizziness",    false) },
        { CharacterAnimator.CharacterAnimationState.Decadent,       ("10_Decadent",     false) },
        { CharacterAnimator.CharacterAnimationState.SkillSelect,     ("11_Skill_Select",  false) },
        { CharacterAnimator.CharacterAnimationState.SkillStart,      ("12_Skill_Start",  false) },
        { CharacterAnimator.CharacterAnimationState.SkillAttak,      ("14_Skill_Attak",  false) },
        { CharacterAnimator.CharacterAnimationState.SkillEnd,        ("15_Skill_End",    false) },
        { CharacterAnimator.CharacterAnimationState.Select,          ("04_Select",       false) }, // 暂不用
        { CharacterAnimator.CharacterAnimationState.Idle,            ("00_Idle",         false) }, // 暂不用
    };

    // ------------------------------------------------------------------ 状态
    private List<CharacterDataSO> allChars = new List<CharacterDataSO>();
    private int selectedIndex = 0;
    private CharacterDataSO SelectedSO => (allChars != null && selectedIndex >= 0 && selectedIndex < allChars.Count) ? allChars[selectedIndex] : null;

    private CharacterAnimator.CharacterAnimationState selectedState = CharacterAnimator.CharacterAnimationState.PlayNormal;

    private Vector3 previewPos = new Vector3(0f, 0f, 0f);
    private float previewScale = 1f;

    private GameObject previewRoot;          // 当前预览根对象（场景里，带 [CharPreview] 前缀）
    private string previewCharId = "";       // 记录 previewRoot 对应哪个角色，便于换角色时重建
    private string availabilityReport = "";

    // ------------------------------------------------------------------ 菜单 / 生命周期
    [MenuItem("Tools/Musical-Sprite/角色静帧预览")]
    public static void Open()
    {
        var w = GetWindow<CharacterStaticPreviewWindow>("角色静帧预览");
        w.minSize = new Vector2(420, 520);
        w.Show();
    }

    private void OnEnable()
    {
        RefreshCharacterList();
        EditorApplication.update += RepaintSceneWhilePreviewing;
        // 自动带入 Project 选中的角色 SO
        if (Selection.activeObject is CharacterDataSO so && allChars.Contains(so))
            selectedIndex = allChars.IndexOf(so);
    }

    private void OnDisable()
    {
        EditorApplication.update -= RepaintSceneWhilePreviewing;
    }

    private void OnDestroy()
    {
        ClearPreview();
    }

    private void RepaintSceneWhilePreviewing()
    {
        if (previewRoot != null) SceneView.RepaintAll();
    }

    // ------------------------------------------------------------------ GUI
    private void OnGUI()
    {
        DrawCharacterPicker();
        EditorGUILayout.Space(4);
        DrawCharInfo();
        EditorGUILayout.Space(4);
        DrawStatePicker();
        EditorGUILayout.Space(4);
        DrawPlacement();
        EditorGUILayout.Space(4);
        DrawAvailability();
    }

    // -------------------------------------------------------- ① 选角色
    private void DrawCharacterPicker()
    {
        EditorGUILayout.LabelField("① 选择角色", EditorStyles.boldLabel);

        if (allChars.Count == 0)
        {
            EditorGUILayout.HelpBox("Assets/Data/Characters 下没找到 CharacterDataSO。", MessageType.Warning);
            if (GUILayout.Button("刷新列表")) RefreshCharacterList();
            return;
        }

        var names = allChars.Select(c => string.Format("{0}  (id{1}{2})",
            string.IsNullOrEmpty(c.displayName) ? c.name : c.displayName,
            c.characterId,
            c.isPlayer ? " ·玩家" : "")).ToArray();

        EditorGUI.BeginChangeCheck();
        int idx = EditorGUILayout.Popup("角色", selectedIndex, names);
        if (EditorGUI.EndChangeCheck())
        {
            if (idx != selectedIndex)
            {
                selectedIndex = idx;
                // 换角色 → 销毁旧预览，下次生成用新模型
                if (previewRoot != null && previewCharId != CurrentCharId) ClearPreview();
                BuildAvailabilityReport();
            }
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("刷新列表")) RefreshCharacterList();
            if (GUILayout.Button("从 Project 选择带入"))
            {
                if (Selection.activeObject is CharacterDataSO so && allChars.Contains(so))
                {
                    selectedIndex = allChars.IndexOf(so);
                    BuildAvailabilityReport();
                }
            }
        }
    }

    // -------------------------------------------------------- ② 角色信息
    private void DrawCharInfo()
    {
        EditorGUILayout.LabelField("② 角色信息（只读）", EditorStyles.boldLabel);
        var so = SelectedSO;
        if (so == null) { EditorGUILayout.HelpBox("未选择角色。", MessageType.Info); return; }

        EditorGUILayout.LabelField("显示名", string.IsNullOrEmpty(so.displayName) ? "（空）" : so.displayName);
        EditorGUILayout.LabelField("动画前缀", string.IsNullOrEmpty(so.animationPrefix) ? "（空）" : so.animationPrefix);
        EditorGUILayout.LabelField("外观预制体", so.modelPrefab != null ? so.modelPrefab.name : "（未填，无法预览）");
        EditorGUILayout.LabelField("音轨 / 玩家", so.isPlayer ? "玩家自身" : ("lane " + so.laneIndex));
    }

    // -------------------------------------------------------- ③ 动画状态
    private void DrawStatePicker()
    {
        EditorGUILayout.LabelField("③ 动画状态（静帧）", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        selectedState = (CharacterAnimator.CharacterAnimationState)EditorGUILayout.EnumPopup("动画状态", selectedState);
        if (EditorGUI.EndChangeCheck() && previewRoot != null)
        {
            Pose(); // 已生成则直接换姿势，不重建
        }

        bool isLoop = StateSuffix.TryGetValue(selectedState, out var s) && s.loop;
        EditorGUILayout.HelpBox(
            isLoop
                ? "该状态是 loop：Scene 视图里会自动循环播放（Spine [ExecuteAlways]）。"
                : "该状态是 oneshot：Scene 视图里播放一次后停在末帧（静帧）。想重看可再点一次「生成/刷新预览」。",
            MessageType.None);
    }

    // -------------------------------------------------------- ④ 场景摆放
    private void DrawPlacement()
    {
        EditorGUILayout.LabelField("④ 场景摆放（不进 Play）", EditorStyles.boldLabel);

        previewPos = EditorGUILayout.Vector3Field("位置", previewPos);
        previewScale = EditorGUILayout.FloatField("缩放", Mathf.Max(0.001f, previewScale));

        // 位置/缩放变化实时反映到已生成的预览对象
        if (previewRoot != null)
        {
            previewRoot.transform.position = previewPos;
            previewRoot.transform.localScale = Vector3.one * previewScale;
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.backgroundColor = new Color(0.65f, 0.95f, 0.65f);
            if (GUILayout.Button("生成 / 刷新预览", GUILayout.Height(30))) ShowOrRefresh();
            GUI.backgroundColor = Color.white;

            GUI.backgroundColor = new Color(1f, 0.75f, 0.7f);
            if (GUILayout.Button("清除预览", GUILayout.Height(30))) ClearPreview();
            GUI.backgroundColor = Color.white;
        }

        if (GUILayout.Button("清除全部 [CharPreview]"))
        {
            ClearAll();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.HelpBox(
            "生成后：在 Hierarchy 选中预览对象，用 Unity 移动/缩放工具调位置大小；或直接改上方位置/缩放字段。\n" +
            "确认完务必「清除预览」再提交（临时对象会标记场景为脏）。",
            MessageType.Warning);
    }

    // -------------------------------------------------------- ⑤ 动画可用性报告
    private void DrawAvailability()
    {
        EditorGUILayout.LabelField("⑤ 动画可用性（前缀+后缀 是否在 Spine 里存在）", EditorStyles.boldLabel);
        if (SelectedSO == null || SelectedSO.modelPrefab == null)
        {
            EditorGUILayout.HelpBox("先选一个已填 modelPrefab 的角色。", MessageType.Info);
            return;
        }
        if (string.IsNullOrEmpty(availabilityReport))
            BuildAvailabilityReport();
        EditorGUILayout.HelpBox(availabilityReport, MessageType.None);
    }

    // ------------------------------------------------------------------ 逻辑
    private void RefreshCharacterList()
    {
        allChars = new List<CharacterDataSO>();
        var guids = AssetDatabase.FindAssets("t:CharacterDataSO", new[] { "Assets/Data/Characters" });
        foreach (var g in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(g);
            var so = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(path);
            if (so != null) allChars.Add(so);
        }
        allChars = allChars.OrderBy(c => c.characterId).ToList();
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, allChars.Count - 1));
        BuildAvailabilityReport();
    }

    private string CurrentCharId => SelectedSO != null ? SelectedSO.name : "";

    /// <summary>生成或刷新预览：若当前预览不是这个角色，重建；否则只换姿势。</summary>
    private void ShowOrRefresh()
    {
        var so = SelectedSO;
        if (so == null) { ShowNotification(new GUIContent("先选角色")); return; }
        if (so.modelPrefab == null) { ShowNotification(new GUIContent("该角色 modelPrefab 未填，无法预览")); return; }

        if (previewRoot == null || previewCharId != CurrentCharId)
        {
            // 重建：销毁旧对象，实例化新模型
            if (previewRoot != null) DestroyImmediate(previewRoot);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(so.modelPrefab);
            if (go == null) go = Object.Instantiate(so.modelPrefab);
            if (go == null) { ShowNotification(new GUIContent("实例化失败")); return; }

            go.name = Tag + " " + (string.IsNullOrEmpty(so.displayName) ? so.name : so.displayName);
            go.transform.position = previewPos;
            go.transform.localScale = Vector3.one * previewScale;

            previewRoot = go;
            previewCharId = CurrentCharId;

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
            FramePreview();
        }
        else
        {
            // 复用：仅更新变换
            previewRoot.transform.position = previewPos;
            previewRoot.transform.localScale = Vector3.one * previewScale;
        }

        Pose();
        ShowNotification(new GUIContent("已生成，看 Scene 视图"));
    }

    /// <summary>用 SkeletonAnimation.AnimationName 驱动 Spine 摆出 selectedState 的静帧（编辑器安全，[ExecuteAlways] 自动播放）。</summary>
    private void Pose()
    {
        if (previewRoot == null) return;
        var so = SelectedSO;
        if (so == null) return;
        if (!StateSuffix.TryGetValue(selectedState, out var map)) return;

        var skel = previewRoot.GetComponentInChildren<SkeletonAnimation>(true);
        if (skel == null)
        {
            Debug.LogWarning("[CharPreview] 预览对象里找不到 SkeletonAnimation 子组件。");
            return;
        }

        string full = so.animationPrefix + "_" + map.suffix;
        // 确认 Spine 里确有这个动画；没有则 graceful 跳过并提示
        var sd = (skel.SkeletonDataAsset != null) ? skel.SkeletonDataAsset.GetSkeletonData(true) : null;
        if (sd == null || sd.FindAnimation(full) == null)
        {
            Debug.LogWarning($"[CharPreview] 前缀={so.animationPrefix} 在 SkeletonData 中找不到动画：{full}（该状态不会摆出）");
            ShowNotification(new GUIContent("动画缺失：" + full));
            return;
        }

        // 编辑器下驱动 Spine 的标准做法（见 ShitSpineSetup）：设 AnimationName + loop，[ExecuteAlways] 自动播放
        skel.AnimationName = full;
        skel.loop = map.loop;
    }

    private void FramePreview()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv != null && previewRoot != null)
        {
            Selection.activeGameObject = previewRoot;
            sv.FrameSelected();
        }
    }

    private void ClearPreview()
    {
        if (previewRoot != null)
        {
            Undo.DestroyObjectImmediate(previewRoot);
            previewRoot = null;
            previewCharId = "";
        }
    }

    private void ClearAll()
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        int n = 0;
        for (int i = 0; i < all.Length; i++)
        {
            var go = all[i];
            if (go == null) continue;
            if (!go.scene.IsValid()) continue;          // 只清场景里，不动资产
            if (!go.name.StartsWith(Tag)) continue;
            Undo.DestroyObjectImmediate(go);
            n++;
        }
        previewRoot = null;
        previewCharId = "";
        Debug.Log($"[CharPreview] 清除了 {n} 个预览对象");
        ShowNotification(new GUIContent("清除 " + n + " 个"));
    }

    /// <summary>生成「前缀+后缀 哪些在 Spine 里存在」的报告，直接验证统一模板的命名约定。</summary>
    private void BuildAvailabilityReport()
    {
        var so = SelectedSO;
        if (so == null) { availabilityReport = "（未选择角色）"; return; }
        if (so.modelPrefab == null) { availabilityReport = "（该角色 modelPrefab 未填，无法校验）"; return; }
        if (string.IsNullOrEmpty(so.animationPrefix)) { availabilityReport = "（animationPrefix 为空，无法校验）"; return; }

        // 用 prefab 里的 SkeletonDataAsset 校验
        var skel = (so.modelPrefab != null) ? so.modelPrefab.GetComponentInChildren<SkeletonAnimation>(true) : null;
        var sd = (skel != null && skel.SkeletonDataAsset != null) ? skel.SkeletonDataAsset.GetSkeletonData(true) : null;
        if (sd == null)
        {
            availabilityReport = $"（无法读取 {so.modelPrefab.name} 的 SkeletonData，请确认 Spine 资源已导入）";
            return;
        }

        var found = new List<string>();
        var missing = new List<string>();
        foreach (var kv in StateSuffix)
        {
            string full = so.animationPrefix + "_" + kv.Value.suffix;
            if (sd.FindAnimation(full) != null) found.Add(full);
            else missing.Add(full);
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"角色：{so.displayName}　前缀：{so.animationPrefix}");
        sb.AppendLine($"✅ 存在 {found.Count} / ❌ 缺失 {missing.Count}（共 {StateSuffix.Count} 个标准槽位）");
        if (missing.Count > 0)
            sb.AppendLine("缺失（战斗里对应状态不会播放，需补 Spine 动画或改 SlotTable 后缀）：\n  " + string.Join("\n  ", missing));
        availabilityReport = sb.ToString().TrimEnd();
    }
}
#endif
