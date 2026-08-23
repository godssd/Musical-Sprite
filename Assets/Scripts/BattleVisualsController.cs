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
    public ComboDisplay comboDisplay;

    [Header("乐队成员")]
    public Transform leftBandRoot;
    public Transform rightBandRoot;

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
        if (rank == "PERFECT" || rank == "GOOD")
        {
            if (comboDisplay != null) comboDisplay.AddCombo(1);
        }
        else if (rank == "MISS")
        {
            if (comboDisplay != null) comboDisplay.ResetCombo();
        }
    }
}
