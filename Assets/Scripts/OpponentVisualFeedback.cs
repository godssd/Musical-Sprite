using UnityEngine;

/// <summary>
/// 对手按键的视觉反馈。
/// 当 AI（或未来的网络对手）按下某条轨道时，让该轨道对应的真实指示灯（LaneIndicator）短暂亮起，
/// 表现与本地玩家按键完全一致。
/// </summary>
public class OpponentVisualFeedback : MonoBehaviour
{
    [Header("引用")]
    public OpponentInput opponentInput;
    public NoteSpawner rightSpawner;

    [Header("指示灯")]
    [Tooltip("右侧各轨指示灯（与场景里 RightBand_Indicator_Lane0..3 对应），由 Start 自动查找")]
    public LaneIndicator[] rightIndicators = new LaneIndicator[4];

    void Start()
    {
        if (opponentInput != null)
            opponentInput.OnPressLane += OnOpponentPress;

        // 若未手动指定，则按场景命名约定自动查找右侧指示灯
        if (rightSpawner != null)
        {
            for (int lane = 0; lane < rightIndicators.Length; lane++)
            {
                if (rightIndicators[lane] == null)
                {
                    GameObject go = GameObject.Find($"RightBand_Indicator_Lane{lane}");
                    if (go != null) rightIndicators[lane] = go.GetComponent<LaneIndicator>();
                }
            }
        }
    }

    private void OnOpponentPress(int lane)
    {
        if (rightSpawner == null || lane < 0 || lane >= rightIndicators.Length) return;
        if (rightIndicators[lane] != null)
        {
            rightIndicators[lane].Flash();
        }
    }

    void OnDestroy()
    {
        if (opponentInput != null)
            opponentInput.OnPressLane -= OnOpponentPress;
    }
}
