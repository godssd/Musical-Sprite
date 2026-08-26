using UnityEngine;

/// <summary>
/// 战斗视觉总控。
/// 管理：轨道指示灯、乐队成员、连击数显示。
/// 订阅 NoteSpawner 的输入和判定事件。
/// </summary>
public class BattleVisualsController : MonoBehaviour
{
    [Header("发射器")]
    public NoteSpawner leftSpawner;
    public NoteSpawner rightSpawner;

    [Header("轨道指示灯（side * 4 + lane）")]
    public LaneIndicator[] indicators = new LaneIndicator[8];

    [Header("连击显示")]
    public ComboDisplay leftComboDisplay;
    public ComboDisplay rightComboDisplay;

    [Header("乐队成员")]
    public Transform leftBandRoot;
    public Transform rightBandRoot;

    [Header("中间粉杠")]
    public BattleCenterLine centerLine;

    /// <summary>按 (side, lane) 缓存对应 CharacterCubeMarker（启动时一次性扫描，OnLanePress 直接调用 O(1)）。</summary>
    private CharacterCubeMarker[] markerByKey = new CharacterCubeMarker[8]; // side*4 + lane

    private void Start()
    {
        if (leftSpawner != null)
        {
            leftSpawner.OnLanePress += OnLanePress;
            leftSpawner.OnJudge += OnJudge;
        }
        if (rightSpawner != null)
        {
            rightSpawner.OnLanePress += OnLanePress;
            rightSpawner.OnJudge += OnJudge;
        }

        // 把世界定位所需引用交给 ComboDisplay
        if (leftComboDisplay != null)
        {
            leftComboDisplay.spawner = leftSpawner;
            if (centerLine != null) leftComboDisplay.centerLine = centerLine;
            leftComboDisplay.side = 0;
        }
        if (rightComboDisplay != null)
        {
            rightComboDisplay.spawner = rightSpawner;
            if (centerLine != null) rightComboDisplay.centerLine = centerLine;
            rightComboDisplay.side = 1;
        }

        // 预缓存标记（玩家自身 cube laneIndex=-1 不进缓存，OnLanePress 不会涉及）
        BuildMarkerCache();
    }

    private void OnDestroy()
    {
        if (leftSpawner != null)
        {
            leftSpawner.OnLanePress -= OnLanePress;
            leftSpawner.OnJudge -= OnJudge;
        }
        if (rightSpawner != null)
        {
            rightSpawner.OnLanePress -= OnLanePress;
            rightSpawner.OnJudge -= OnJudge;
        }
    }

    /// <summary>
    /// 建立 (side, lane) → 角色 cube 的缓存。
    /// 优先用场景里已挂好的 CharacterCubeMarker；若缺失（例如尚未用 MS Debug 工具补 marker），
    /// 则按命名 Left/RightBand_Member_LaneN 自动补上组件，保证左右两侧按键反馈都一定生效。
    /// </summary>
    private void BuildMarkerCache()
    {
        // 1) 先收集场景里已有的 marker
        var existing = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        for (int i = 0; i < existing.Length; i++)
        {
            var m = existing[i];
            if (m == null || m.laneIndex < 0 || m.laneIndex > 3) continue; // 玩家自身不在 lane 索引里
            int key = Mathf.Clamp(m.side, 0, 1) * 4 + Mathf.Clamp(m.laneIndex, 0, 3);
            markerByKey[key] = m;
        }

        // 2) 兜底：扫描所有 transform，按命名自动补 marker（无需手动在场景里挂组件）
        var all = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (t == null) continue;
            string n = t.name;
            if (!n.Contains("Member_Lane")) continue; // 只处理队伍角色，跳过 Indicator / Protagonist
            int side = n.StartsWith("LeftBand") ? 0 : (n.StartsWith("RightBand") ? 1 : -1);
            if (side < 0) continue;

            int laneIdx = n.LastIndexOf("Lane");
            if (laneIdx < 0 || laneIdx + 4 >= n.Length) continue;
            if (!int.TryParse(n.Substring(laneIdx + 4), out int lane)) continue;
            if (lane < 0 || lane > 3) continue;

            int key = side * 4 + lane;
            if (markerByKey[key] != null) continue; // 已有则跳过

            var m = t.gameObject.GetComponent<CharacterCubeMarker>();
            if (m == null) m = t.gameObject.AddComponent<CharacterCubeMarker>();
            m.side = side;
            m.laneIndex = lane;
            markerByKey[key] = m;
        }
    }

    private void OnLanePress(int side, int lane)
    {
        if (lane < 0 || lane >= 4 || side < 0 || side > 1) return;
        int idx = side * 4 + lane;
        if (idx < indicators.Length && indicators[idx] != null)
        {
            indicators[idx].Flash();
        }
        // 角色 cube 的反馈改为"命中判定时"才闪（见 OnJudge），避免按键阶段误闪；
        // 按键阶段只保留轨道指示灯闪烁。
    }

    private void OnJudge(int side, int lane, string rank, Vector3 position, UnityEngine.Object source)
    {
        ComboDisplay target = side == 0 ? leftComboDisplay : rightComboDisplay;
        if (target == null) return;

        if (rank == "MISS")
        {
            target.ResetCombo();
        }
        else
        {
            // PERFECT / GOOD / CLEAR 都记一次连击。
            // 一个完整长按 = 起手命中 +1、完成时 CLEAR +1，共 +2。
            target.AddCombo(1);
        }

        // 命中角色闪烁（修复①"平时命中角色不闪"）：
        // - 普通音符命中 -> 该 (side, lane) 队伍角色闪烁
        // - 被附魔的音符命中 -> 不闪该 lane 角色，改由施法大狗闪（见 ActiveSkillRuntime 成功分支 PulseGlow）
        if (rank != "MISS")
        {
            bool charmed = false;
            if (source is Note n) charmed = n.wasCharmed;
            else if (source is HoldNote h) charmed = h.wasCharmed;
            if (!charmed)
            {
                int idx = side * 4 + lane;
                if (idx >= 0 && idx < markerByKey.Length && markerByKey[idx] != null)
                    markerByKey[idx].Flash();
            }
        }
    }
}
