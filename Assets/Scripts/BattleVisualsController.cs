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

    private void Start()
    {
        if (leftSpawner != null)
        {
            leftSpawner.OnLanePress += OnLanePress;
            leftSpawner.OnLaneUp += OnLaneUp;
            leftSpawner.OnJudge += OnJudge;
        }
        if (rightSpawner != null)
        {
            rightSpawner.OnLanePress += OnLanePress;
            rightSpawner.OnLaneUp += OnLaneUp;
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
    }

    private void OnDestroy()
    {
        if (leftSpawner != null)
        {
            leftSpawner.OnLanePress -= OnLanePress;
            leftSpawner.OnLaneUp -= OnLaneUp;
            leftSpawner.OnJudge -= OnJudge;
        }
        if (rightSpawner != null)
        {
            rightSpawner.OnLanePress -= OnLanePress;
            rightSpawner.OnLaneUp -= OnLaneUp;
            rightSpawner.OnJudge -= OnJudge;
        }
    }

    private void OnLanePress(int side, int lane)
    {
        if (lane < 0 || lane >= 4 || side < 0 || side > 1) return;
        int idx = side * 4 + lane;
        if (idx < indicators.Length && indicators[idx] != null)
        {
            indicators[idx].Hold(true);   // 按下即亮，长按期间持续亮
        }
        // 按键阶段只闪轨道指示灯；角色 cube 的发光表现现在只来自主动技能相关事件
        // （技能输入按键按对: SkillInputUI；被附魔音符命中: ActiveSkillRuntime.PulseGlow），
        // 普通音符命中不再触发角色发光。
    }

    private void OnLaneUp(int side, int lane)
    {
        if (lane < 0 || lane >= 4 || side < 0 || side > 1) return;
        int idx = side * 4 + lane;
        if (idx < indicators.Length && indicators[idx] != null)
        {
            indicators[idx].Hold(false);  // 松开即灭
        }
    }

    private void OnJudge(int side, int lane, string rank, Vector3 position, UnityEngine.Object source)
    {
        // 沉睡期间该侧无命中表现（输入已被屏蔽，此处作双保险，且避免沉睡中残留连击跳动）
        if (SleepController.Instance != null && SleepController.Instance.IsSideSleeping(side)) return;

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

        // 普通命中：对应音轨角色向上跳一下（修复：之前被误删，导致命中时角色不跳）。
        // 释放主动技能期间屏蔽该表现（ActiveSkillRuntime 维护 CastingSides 集合）。
        if (rank != "MISS" && !ActiveSkillRuntime.IsSideCasting(side))
        {
            var m = CharacterCubeMarker.GetAt(side, lane);
            if (m != null) m.Jump();
        }

        // 注意：普通音符命中不再让角色 cube 发光闪烁（用户确认：普通音符不需要该表现）。
        // 角色发光只保留两类来源：
        //   1) 主动技能输入按键按对 -> SkillInputUI.rt.marker.Flash()
        //   2) 被附魔音符命中（施法角色）-> ActiveSkillRuntime 成功分支 PulseGlow()
    }
}
