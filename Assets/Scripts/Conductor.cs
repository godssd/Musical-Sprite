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

    [Header("只读状态")]
    [SerializeField] private float _songPosition;           // 当前歌曲时间（秒）
    [SerializeField] private float _songPositionInBeats;  // 当前拍数

    public float songPosition => _songPosition;
    public float songPositionInBeats => _songPositionInBeats;

    private double dspStartTime;
    private bool isPlaying;

    void Start()
    {
        if (musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        // 无论有没有音乐 clip，都启动计时。没有 clip 时只跑判定系统。
        dspStartTime = AudioSettings.dspTime;
        isPlaying = true;

        if (musicSource != null && musicSource.clip != null)
        {
            musicSource.Play();
        }
    }

    void Update()
    {
        if (!isPlaying) return;

        _songPosition = (float)(AudioSettings.dspTime - dspStartTime) - songOffset;
        _songPositionInBeats = _songPosition / secPerBeat;
    }

    /// <summary>
    /// 手动开始播放，用于需要延迟启动的场景。
    /// </summary>
    public void Play()
    {
        if (musicSource == null) return;

        dspStartTime = AudioSettings.dspTime;
        musicSource.Play();
        isPlaying = true;
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
