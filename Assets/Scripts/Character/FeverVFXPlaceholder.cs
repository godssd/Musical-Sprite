using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 旧过热占位组件。屏幕半幅染色已移除，过热布景由 FeverSceneryController 控制。
/// 此组件仅保留早期队伍方块的主题色逻辑，主场景不再自动创建它。
///
/// 挂载：场景任意 GO（FeverManager 会自动补建）。无正式美术，后续整体替换。
/// </summary>
public class FeverVFXPlaceholder : MonoBehaviour
{
    [Header("主题色")]
    public Color leftTheme = new Color(1f, 0.3f, 0.3f);    // 红方
    public Color rightTheme = new Color(0.3f, 0.6f, 1f);   // 蓝方
    public Color superColor = new Color(1f, 0.85f, 0.1f);  // 超级过热（金）

    [Header("队伍方块（按 side 自动绑定 CharacterCubeMarker）")]
    public List<Renderer> leftTeamCubes = new List<Renderer>();
    public List<Renderer> rightTeamCubes = new List<Renderer>();

    // 角色身份色快照：进入过热前先抓一次，过热时只“向主题色混合”，不整体覆盖成白/红/蓝（否则谁是谁就分不清了）
    private List<Color> leftTeamCubeIdentity = new List<Color>();
    private List<Color> rightTeamCubeIdentity = new List<Color>();
    private bool identityCaptured = false;

    private FeverManager fever;

    void Start()
    {
        // 用单例订阅，规避与 ScoreManager 运行时补建 FeverManager 的 Start 顺序竞争；
        // Instance 未就绪时延迟兜底（同 CharacterBattleSystem）
        if (FeverManager.Instance != null) SubscribeFever();
        else StartCoroutine(SubscribeFeverDelayed());
    }

    private void SubscribeFever()
    {
        fever = FeverManager.Instance;
        if (fever == null)
        {
            Debug.LogWarning("[FeverVFXPlaceholder] 未找到 FeverManager");
            return;
        }
        if (leftTeamCubes.Count == 0 && rightTeamCubes.Count == 0)
            AutoFindCubes();
        fever.OnStateChanged += OnFeverChanged;
    }

    private System.Collections.IEnumerator SubscribeFeverDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        SubscribeFever();
    }

    void OnDestroy()
    {
        if (fever != null) fever.OnStateChanged -= OnFeverChanged;
    }

    private void AutoFindCubes()
    {
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        if (markers.Length == 0)
            return; // 场景里没人手动补 marker，正常（CharacterBattleSystem 启动时也会再尝试一次）
        identityCaptured = false; // 列表可能变化，下次 OnFeverChanged 重新抓身份色
        foreach (var m in markers)
        {
            if (m == null) continue;
            if (m.IsPlayer) continue; // 玩家自身不参与队伍方块材质变色
            var r = m.GetComponent<Renderer>();
            if (r == null) continue;
            if (m.side == 0) leftTeamCubes.Add(r);
            else if (m.side == 1) rightTeamCubes.Add(r);
        }
    }

    /// <summary>抓一次队伍方块的当前颜色作为“身份色”快照（在第一次过热变化前调用）。</summary>
    private void CaptureIdentities()
    {
        leftTeamCubeIdentity.Clear();
        rightTeamCubeIdentity.Clear();
        foreach (var r in leftTeamCubes) leftTeamCubeIdentity.Add(r != null ? r.material.color : Color.white);
        foreach (var r in rightTeamCubes) rightTeamCubeIdentity.Add(r != null ? r.material.color : Color.white);
    }

    private void OnFeverChanged(FeverState oldState, FeverState cur, int side)
    {
        // 队伍方块材质变色：保留角色身份色，过热时仅“向主题色混合 60%”（不再整体覆盖成白/红/蓝，避免分不清谁是谁）
        var cubes = side == 0 ? leftTeamCubes : rightTeamCubes;
        var identities = side == 0 ? leftTeamCubeIdentity : rightTeamCubeIdentity;
        if (!identityCaptured)
        {
            CaptureIdentities();
            identityCaptured = true;
        }
        Color feverTint = cur == FeverState.None ? Color.clear :
                          cur == FeverState.SuperFever ? superColor : (side == 0 ? leftTheme : rightTheme);
        for (int i = 0; i < cubes.Count; i++)
        {
            var r = cubes[i];
            if (r == null) continue;
            Color baseCol = (i < identities.Count) ? identities[i] : r.material.color;
            // None：保持身份色；Fever/Super：身份色 → 主题色 混合 60%
            Color target = (cur == FeverState.None) ? baseCol : Color.Lerp(baseCol, feverTint, 0.6f);
            r.material.color = target;
        }
    }

}
