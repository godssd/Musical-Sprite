using UnityEngine;

/// <summary>
/// 谱面数据资产。把歌曲所有音符放在这里，方便非程序人员编辑。
/// </summary>
[CreateAssetMenu(fileName = "NewBeatmap", menuName = "Musical Sprite/Beatmap", order = 1)]
public class BeatmapSO : ScriptableObject
{
    [Tooltip("整首歌的音符列表。time 必须按升序排列。")]
    public NoteData[] notes;

    [Tooltip("歌曲 BPM，用于辅助编辑")]
    public float bpm = 120f;
}
