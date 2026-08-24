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

    private void OnLanePress(int side, int lane)
    {
        if (lane < 0 || lane >= 4 || side < 0 || side > 1) return;
        int idx = side * 4 + lane;
        if (idx < indicators.Length && indicators[idx] != null)
        {
            indicators[idx].Flash();
        }
    }

    private void OnJudge(int side, int lane, string rank, Vector3 position)
    {
        ComboDisplay target = side == 0 ? leftComboDisplay : rightComboDisplay;

        if (rank == "PERFECT" || rank == "GOOD")
        {
            if (target != null) target.AddCombo(1);
        }
        else if (rank == "MISS")
        {
            if (target != null) target.ResetCombo();
        }
    }
}
