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

    [Tooltip("导入期（秒）：起播前预留的保底静默。若全场都是无开场动画的占位角色，由它保证开头不突兀；有开场动画时静默期=最长开场动画与它取大。用主线程时钟计时，同样抗卡顿。")]
    public float leadIn = 1f;

    [Tooltip("起播缓冲（秒）：检测到\"全员开场动画播完\"后，到正式出声的固定延迟。保证 PlayScheduled 调度精度，并吸收检测帧后的零星小卡顿。")]
    public float startDelay = 0.5f;

    [Header("只读状态")]
    [SerializeField] private float _songPosition;           // 当前歌曲时间（秒）
    [SerializeField] private float _songPositionInBeats;  // 当前拍数

    public float songPosition => _songPosition;
    public float songPositionInBeats => _songPositionInBeats;

    private double dspStartTime;
    private bool isPlaying;
    private bool playbackScheduled; // 防止重复调度起播（armed 模式与旧路径共用）
    private bool playbackArmed;     // 待命模式：等待就绪条件满足后自动 PlayScheduled
    private float sceneStartUnityTime; // 场景加载时刻（主线程 Time.time），用于 leadIn 保底静默计时

    /// <summary>就绪条件提供者：由 GameManager 注入（全员开场动画播完）。null 时视作始终就绪（跳过开场门控）。</summary>
    public System.Func<bool> introReadyProvider;

    void Start()
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }
        sceneStartUnityTime = Time.time;

        // 有 clip：走"开场动画兜底"待命模式（等全员开场播完 + leadIn 保底，再 PlayScheduled 精确起播）。
        // 没有 clip：只跑判定时钟（songPosition 从 0 前进，无音乐），保持旧行为供纯判定调试。
        if (musicSource != null && musicSource.clip != null)
        {
            ArmPlayback();
        }
        else
        {
            dspStartTime = AudioSettings.dspTime;
            isPlaying = true;
        }
    }

    /// <summary>
    /// [推荐路径] 待命起播：本方法只"上膛"，不立即出声。Update 每帧检查就绪条件——
    /// (1) introReadyProvider 返回 true（全员开场动画播完，最长的角色成为静默期标准）；
    /// (2) 场景已过 leadIn 保底静默（主线程时钟，抗卡顿）。
    /// 满足那一刻以 PlayScheduled(dspTime + startDelay) 把时钟锚点与真实出声时刻预定到同一 DSP 时刻。
    /// 待命期间 songPosition 钉在 -1（NoteSpawner 生成条件天然不满足，不会倾倒逾期音符）；
    /// 卡顿期间主线程冻结 → 开场动画同样冻结 → 起播自动推迟，结构上不可能吞掉歌曲开头。
    /// </summary>
    public void ArmPlayback()
    {
        playbackArmed = true;
        playbackScheduled = false;
        isPlaying = true;
    }

    private bool IsIntroGateReady()
    {
        bool introsDone = (introReadyProvider == null) || introReadyProvider();
        bool floorElapsed = (Time.time - sceneStartUnityTime) >= leadIn;
        return introsDone && floorElapsed;
    }

    void Update()
    {
        if (!isPlaying) return;

        // 待命中：等就绪条件满足再调度起播；期间 songPosition 钉负，不生成音符
        if (playbackArmed && !playbackScheduled)
        {
            if (!IsIntroGateReady())
            {
                _songPosition = -1f;
                _songPositionInBeats = -1f;
                return;
            }

            double scheduledStart = AudioSettings.dspTime + System.Math.Max(0f, startDelay);
            dspStartTime = scheduledStart;
            if (musicSource != null && musicSource.clip != null)
            {
                musicSource.PlayScheduled(scheduledStart);
            }
            playbackScheduled = true;
        }

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
        playbackArmed = false;
    }
}
