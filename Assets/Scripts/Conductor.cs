using UnityEngine;

/// <summary>
/// 音乐时钟：整个游戏的时间基准。
/// 使用 AudioSettings.dspTime 而不是 Time.time，避免帧率波动影响判定。
/// </summary>
public class Conductor : MonoBehaviour
{
    [Header("音频源")]
    public AudioSource musicSource;

    [Header("歌曲信息")]
    [Tooltip("每拍多少秒。BPM = 120 时，secPerBeat = 60/120 = 0.5")]
    public float secPerBeat = 0.5f;

    [Tooltip("歌曲偏移（秒）。正数表示提前播放，负数表示延后播放。用于校准设备延迟。")]
    public float songOffset = 0f;

    [Tooltip("导入期（秒）：起播前预留的缓冲时间。场景加载/首帧卡顿会被这段缓冲吸收，歌曲本体零损失；此期间 songPosition 为负数、不生成音符。")]
    public float leadIn = 3f;

    [Header("只读状态")]
    [SerializeField] private float _songPosition;           // 当前歌曲时间（秒）
    [SerializeField] private float _songPositionInBeats;  // 当前拍数

    public float songPosition => _songPosition;
    public float songPositionInBeats => _songPositionInBeats;

    private double dspStartTime;
    private bool isPlaying;
    private bool playbackScheduled; // 防止 Conductor.Start 与 GameManager.Start 执行顺序不确定时重复调度起播

    void Start()
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        // 无论有没有音乐 clip，都启动计时。没有 clip 时只跑判定系统。
        if (musicSource != null && musicSource.clip != null)
        {
            if (!playbackScheduled) StartPlaybackWithLeadIn(leadIn);
        }
        else
        {
            dspStartTime = AudioSettings.dspTime;
            isPlaying = true;
        }
    }

    /// <summary>
    /// 精确起播（推荐）：把时钟锚点和真实出声时刻都预定到 leadInSeconds 之后的 DSP 时刻。
    /// 修复两个不同步根因：
    /// 1) 旧 Play() 与 dspTime 锚点不同步（Play 并非立即出声，songPosition 恒定超前音乐）；
    /// 2) 场景加载/首帧卡顿发生在起播之后，卡顿期间时钟照走，导致 songPosition 直接跳到歌曲中间。
    /// leadIn 期间 songPosition 为负数，NoteSpawner 的生成条件（songTime >= hitTime - leadTime）天然不满足，不会倾倒逾期音符。
    /// </summary>
    public void StartPlaybackWithLeadIn(float leadInSeconds = 3f)
    {
        if (musicSource == null) return;

        double scheduledStart = AudioSettings.dspTime + System.Math.Max(0f, leadInSeconds);
        dspStartTime = scheduledStart;
        if (musicSource.clip != null)
        {
            musicSource.PlayScheduled(scheduledStart);
        }
        isPlaying = true;
        playbackScheduled = true;
    }

    /// <summary>
    /// [旧路径·兜底用] 立即起播。有 Play 调度偏差且不吃启动卡顿，正式流程请用 StartPlaybackWithLeadIn。
    /// </summary>
    public void Play()
    {
        if (musicSource == null) return;

        dspStartTime = AudioSettings.dspTime;
        musicSource.Play();
        isPlaying = true;
    }

    void Update()
    {
        if (!isPlaying) return;

        _songPosition = (float)(AudioSettings.dspTime - dspStartTime) - songOffset;
        _songPositionInBeats = _songPosition / secPerBeat;
    }

    /// <summary>
    /// 停止播放。
    /// </summary>
    public void Stop()
    {
        if (musicSource == null) return;
        musicSource.Stop();
        isPlaying = false;
    }
}
