using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 液体血条控制器（配合 Shader "MusicalSprite/UI/HPBarLiquid" 使用）。
///
/// 层级要求（从底到顶）：
///   HPBarRoot（挂本脚本）
///     ├── Decoration   (Image, HPBar_decoration.png，深红底边，可选)
///     ├── Background   (Image, HPBar_Container.png，黑底)
///     ├── Ghost        (Image, HPBar_Container.png，黄色材质，受击残影)
///     ├── Fill         (Image, HPBar_Container.png，液体 Shader)
///     ├── FlashOverlay (Image, HPBar_Container.png，白色，受击闪白)
///     ├── Frame        (Image, HPBar_Frame.png，白描边)
///     └── HPNumber/Text
///
/// 扣血特效（受击瞬间）：
///   1. 整条血条【闪白 + 放大后快速回弹】（强度随伤害量变化）。
///   2. 红色 Fill 按先快后慢曲线跌到新 HP。
///   3. 黄色 Ghost（平涂）先显示"将要扣除的那一段"，保持片刻后快速锐减消失。
///
/// 编辑态预览：用 HPBarFxPreviewWindow（Tools → Musical-Sprite → HP Bar FX Preview）
/// 选中本组件，设起始/目标血量，点"播放扣血"即可在 Scene 视图看到完整序列，无需进 Play。
///
/// side：0 = 左玩家（红，液体靠左）；1 = 右玩家（蓝，镜像）。
/// </summary>
public class HPBarLiquid : MonoBehaviour
{
    [Header("归属")]
    [Tooltip("0 = 左玩家（红），1 = 右玩家（蓝）")]
    public int side = 0;

    [Header("引用")]
    public ScoreManager scoreManager;
    public Image fillImage;
    [Tooltip("黄色残影层（受击时显示被扣除的部分），由 Setup 自动创建")]
    public Image ghostImage;
    [Tooltip("闪白层，由 Setup 自动创建")]
    public Image flashImage;
    public Text hpText;
    [Tooltip("可选：用图片拼出手绘风 HP 数字。留空则继续用上面的 Text 显示。")]
    public HPNumberSprite hpNumberSprite;

    [Header("液体颜色")]
    [Tooltip("液体颜色（纯色模式下上下相同）")]
    public Color topColor = new Color(1.00f, 0.15f, 0.00f);
    [Tooltip("液体颜色（纯色模式下与 topColor 相同）")]
    public Color bottomColor = new Color(1.00f, 0.15f, 0.00f);
    [Tooltip("液面高光/泡沫颜色")]
    public Color crestColor = new Color(1f, 0.35f, 0.25f);

    [Header("填充动画（先快后慢）")]
    [Tooltip("血量变化总时长（秒），无论伤害大小都保持一致")]
    public float fillDuration = 0.4f;
    [Tooltip("缓动指数，越大开头越快、结尾越慢（建议 2~4）")]
    public float fillEasePower = 3f;

    [Header("摇晃（酒杯式上下晃，可保留或归零）")]
    [Tooltip("扣血基础摇晃幅度")]
    public float impactWave = 0.02f;
    [Tooltip("伤害比例转摇晃幅度的系数")]
    public float damageToShake = 0.25f;
    [Tooltip("最小摇晃幅度，避免小伤害没反应")]
    public float minShake = 0.02f;
    [Tooltip("摇晃幅度上限")]
    public float waveMax = 0.10f;
    [Tooltip("摇晃总时长（秒）")]
    public float shakeDuration = 0.5f;
    [Tooltip("摇晃衰减指数，越大开头衰减越快（建议 2~3）")]
    public float shakeEasePower = 2.2f;
    [Tooltip("波纹频率（纵向密度）")]
    public float waveFreq = 14f;
    [Tooltip("波纹流动速度")]
    public float waveSpeed = 5f;
    [Tooltip("环境微波动幅度（无扣血时的轻微液面起伏）")]
    [Range(0f, 0.03f)] public float ambientWave = 0.010f;
    [Tooltip("高频细纹强度（旧版'扭曲波纹'的来源，调小更自然，0=完全关掉）")]
    [Range(0f, 1f)] public float rippleScale = 0.15f;

    [Header("液面形状")]
    [Range(0.001f, 0.05f)] public float edgeSoftness = 0.004f;
    [Range(0f, 0.3f)] public float crestWidth = 0.08f;
    [Range(0f, 3f)] public float crestIntensity = 1.0f;
    [Range(0f, 2f)] public float surfaceGlow = 0.4f;
    [Tooltip("纯色模式下请保持与液体颜色相同")]
    public Color surfaceDarken = new Color(1.00f, 0.15f, 0.00f);
    [Tooltip("纯色模式下建议 0")]
    [Range(0f, 0.5f)] public float surfaceDarkenRange = 0.0f;

    [Header("扣血特效（闪白 + 放大 + 黄色残影）")]
    [Tooltip("总开关。关掉则只播红色 Fill 下落，没有闪白/放大/黄色残影。")]
    public bool enableDamageFx = true;
    [Tooltip("黄色残影颜色（平涂）")]
    public Color ghostColor = new Color(0.937f, 0.624f, 0.153f); // #EF9F27
    [Tooltip("闪白总时长（秒），极短")]
    public float flashDuration = 0.09f;
    [Tooltip("闪白峰值透明度下限（小伤害）")]
    public float flashIntensityMin = 0.55f;
    [Tooltip("闪白峰值透明度上限（大伤害）")]
    public float flashIntensityMax = 1.0f;
    [Tooltip("整体放大峰值下限（小伤害）")]
    public float punchScaleMin = 1.10f;
    [Tooltip("整体放大峰值上限（大伤害）")]
    public float punchScaleMax = 1.30f;
    [Tooltip("放大回弹总时长（秒）")]
    public float punchDuration = 0.14f;
    [Tooltip("黄色残影保持时长（秒），之后再锐减")]
    public float ghostHold = 0.10f;
    [Tooltip("黄色残影锐减时长（秒）")]
    public float ghostDrainDuration = 0.20f;
    [Tooltip("达到满强度所需的伤害比例（占最大HP）。例如0.2表示掉20%血即满强度闪白/放大。")]
    public float refDamageRatio = 0.2f;

    [Header("调试")]
    [Tooltip("开启后会在 Console 打印血量变化，方便排查“不掉血”问题。确认正常后可关掉。")]
    public bool logHP = true;

    private Material _mat;
    private Material _ghostMat;
    private RectTransform _rootRect;
    private int _maxHP = 1;

    private float _currentFill = 1f;
    private float _targetFill = 1f;

    // 填充动画状态
    private float _fillAnimStart;
    private float _fillAnimEnd;
    private float _fillAnimElapsed;
    private bool _fillAnimating;

    // 摇晃状态
    private float _shakeInitial;
    private float _shakeAmount;
    private float _shakeElapsed;

    // 扣血特效状态
    private float _fxFlashElapsed, _fxFlashDur, _fxFlashPeak;
    private float _fxPunchElapsed, _fxPunchDur, _fxPunchPeak;
    private bool _fxGhostActive;
    private float _fxGhostStart, _fxGhostEnd, _fxGhostElapsed, _fxGhostHold, _fxGhostDrain;

    private static readonly int FillID               = Shader.PropertyToID("_Fill");
    private static readonly int FlipID               = Shader.PropertyToID("_Flip");
    private static readonly int WaveAmpID            = Shader.PropertyToID("_WaveAmp");
    private static readonly int WaveFreqID           = Shader.PropertyToID("_WaveFreq");
    private static readonly int WaveSpeedID          = Shader.PropertyToID("_WaveSpeed");
    private static readonly int RippleScaleID        = Shader.PropertyToID("_RippleScale");
    private static readonly int AmbientWaveID        = Shader.PropertyToID("_AmbientWave");
    private static readonly int TopColorID           = Shader.PropertyToID("_TopColor");
    private static readonly int BottomColorID        = Shader.PropertyToID("_BottomColor");
    private static readonly int CrestColorID         = Shader.PropertyToID("_CrestColor");
    private static readonly int EdgeSoftnessID       = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int CrestWidthID         = Shader.PropertyToID("_CrestWidth");
    private static readonly int CrestIntensityID     = Shader.PropertyToID("_CrestIntensity");
    private static readonly int SurfaceGlowID        = Shader.PropertyToID("_SurfaceGlow");
    private static readonly int SurfaceDarkenID      = Shader.PropertyToID("_SurfaceDarken");
    private static readonly int SurfaceDarkenRangeID = Shader.PropertyToID("_SurfaceDarkenRange");
    private static readonly int TopGlossIntensityID  = Shader.PropertyToID("_TopGlossIntensity");
    private static readonly int BlobIntensityID      = Shader.PropertyToID("_BlobIntensity");

    void Start()
    {
        if (scoreManager == null)
            scoreManager = FindFirstObjectByType<ScoreManager>();

        _rootRect = GetComponent<RectTransform>();
        ResolveFillImage();
        if (ghostImage == null)
        {
            Transform g = transform.Find("Ghost");
            if (g != null) ghostImage = g.GetComponent<Image>();
        }
        if (flashImage == null)
        {
            Transform f = transform.Find("FlashOverlay");
            if (f != null) flashImage = f.GetComponent<Image>();
        }
        if (flashImage != null) SetImageAlpha(flashImage, 0f);

        if (scoreManager != null)
            _maxHP = Mathf.Max(1, scoreManager.GetMaxHP(side));

        SetupMaterial();

        if (hpNumberSprite == null)
            hpNumberSprite = GetComponentInChildren<HPNumberSprite>();

        if (scoreManager != null)
        {
            scoreManager.OnHPChanged += OnHPChanged;
            int hp = side == 0 ? scoreManager.GetLeftHP() : scoreManager.GetRightHP();
            int max = scoreManager.GetMaxHP(side);
            SetHPInstant(hp, max);

            if (logHP)
            {
                Debug.Log($"[HPBarLiquid] '{name}' side={side} 已订阅 OnHPChanged，初始 HP={hp}/{max}，"
                          + $"fill={_currentFill:F3}，fillImage={(fillImage != null ? fillImage.name : "缺失")}，"
                          + $"ghost={(ghostImage != null ? "有" : "无")}，flash={(flashImage != null ? "有" : "无")}，"
                          + $"数字={(hpNumberSprite != null ? "图片" : (hpText != null ? "Text" : "无"))}");
            }
        }
        else if (logHP)
        {
            Debug.LogWarning($"[HPBarLiquid] '{name}' 找不到 ScoreManager，血条不会更新！");
        }
    }

    void Update()
    {
        TickFX(Time.deltaTime);
    }

    void OnDestroy()
    {
        if (scoreManager != null)
            scoreManager.OnHPChanged -= OnHPChanged;

        if (_mat != null) { Destroy(_mat); _mat = null; }
        if (_ghostMat != null) { Destroy(_ghostMat); _ghostMat = null; }
    }

    /// <summary>
    /// 每帧推进所有动画（填充 / 摇晃 / 闪白 / 放大 / 黄色残影）。
    /// 同时被运行时 Update 与编辑态预览窗口调用，因此逻辑集中在这里。
    /// </summary>
    public void TickFX(float dt)
    {
        EnsureMaterials();
        if (_mat == null) return;

        UpdateFillAnimation();
        UpdateShake();

        _mat.SetFloat(FillID, _currentFill);
        _mat.SetFloat(WaveAmpID, _shakeAmount);
        _mat.SetFloat(AmbientWaveID, ambientWave);
        _mat.SetFloat(RippleScaleID, rippleScale);

        // 每帧同步颜色，方便在 Inspector / 预览里实时调色
        _mat.SetColor(TopColorID, topColor);
        _mat.SetColor(BottomColorID, bottomColor);
        _mat.SetColor(CrestColorID, crestColor);
        _mat.SetColor(SurfaceDarkenID, surfaceDarken);
        if (_ghostMat != null)
        {
            _ghostMat.SetColor(TopColorID, ghostColor);
            _ghostMat.SetColor(BottomColorID, ghostColor);
        }

        // 闪白：瞬间到峰值后快速衰减
        if (_fxFlashElapsed < _fxFlashDur)
        {
            _fxFlashElapsed += dt;
            float t = Mathf.Clamp01(_fxFlashElapsed / Mathf.Max(0.001f, _fxFlashDur));
            float a = _fxFlashPeak * Mathf.Pow(1f - t, 1.5f);
            SetImageAlpha(flashImage, a);
        }
        else if (flashImage != null && flashImage.color.a > 0.001f)
        {
            SetImageAlpha(flashImage, 0f);
        }

        // 放大：瞬间到峰值后快速回弹（平方衰减）
        if (_fxPunchElapsed < _fxPunchDur)
        {
            _fxPunchElapsed += dt;
            float p = Mathf.Clamp01(_fxPunchElapsed / Mathf.Max(0.001f, _fxPunchDur));
            float k = 1f - p;
            float s = 1f + (_fxPunchPeak - 1f) * k * k;
            if (_rootRect != null) _rootRect.localScale = Vector3.one * s;
        }
        else if (_rootRect != null && _rootRect.localScale != Vector3.one)
        {
            _rootRect.localScale = Vector3.one;
        }

        // 黄色残影：保持片刻后快速锐减
        if (_fxGhostActive)
        {
            _fxGhostElapsed += dt;
            float fill;
            if (_fxGhostElapsed <= _fxGhostHold)
            {
                fill = _fxGhostStart;
            }
            else
            {
                float t2 = Mathf.Clamp01((_fxGhostElapsed - _fxGhostHold) / Mathf.Max(0.001f, _fxGhostDrain));
                float e = 1f - Mathf.Pow(1f - t2, 2f); // 先快后慢地锐减
                fill = Mathf.Lerp(_fxGhostStart, _fxGhostEnd, e);
                if (t2 >= 1f) { _fxGhostActive = false; fill = 0f; }
            }
            if (_ghostMat != null) _ghostMat.SetFloat(FillID, fill);
        }
    }

    private void EnsureMaterials()
    {
        if (_rootRect == null) _rootRect = GetComponent<RectTransform>();
        if (fillImage == null) ResolveFillImage();
        if (_mat == null) SetupMaterial();

        if (_ghostMat == null && ghostImage != null && ghostImage.material != null)
        {
            _ghostMat = new Material(ghostImage.material);
            _ghostMat.SetColor(TopColorID, ghostColor);
            _ghostMat.SetColor(BottomColorID, ghostColor);
            _ghostMat.SetFloat(FlipID, side == 0 ? 0f : 1f);
            _ghostMat.SetFloat(CrestIntensityID, 0f);
            _ghostMat.SetFloat(SurfaceGlowID, 0f);
            _ghostMat.SetFloat(TopGlossIntensityID, 0f);
            _ghostMat.SetFloat(BlobIntensityID, 0f);
            _ghostMat.SetFloat(SurfaceDarkenRangeID, 0f);
            _ghostMat.SetFloat(AmbientWaveID, 0f);
            _ghostMat.SetFloat(FillID, 0f);
            ghostImage.material = _ghostMat;
        }
    }

    private void ResolveFillImage()
    {
        if (fillImage != null) return;

        Transform t = transform.Find("Fill");
        if (t != null) fillImage = t.GetComponent<Image>();

        if (fillImage == null)
        {
            Debug.LogWarning($"[HPBarLiquid] side={side} 找不到名为 Fill 的子对象，液体血条不会工作。");
        }
        else
        {
            Transform bg = transform.Find("Background");
            if (bg != null && fillImage.transform.GetSiblingIndex() <= bg.GetSiblingIndex())
            {
                fillImage.transform.SetSiblingIndex(bg.GetSiblingIndex() + 1);
            }
        }
    }

    private void SetupMaterial()
    {
        if (fillImage == null) return;

        Shader sh = Shader.Find("MusicalSprite/UI/HPBarLiquid");
        if (sh == null)
        {
            Debug.LogError("[HPBarLiquid] 找不到 Shader 'MusicalSprite/UI/HPBarLiquid'，请确认文件在 Assets/Shaders/ 下。");
            return;
        }

        // 运行时实例化：优先克隆 Setup 已配好的资产（保留红/蓝配色），避免左右共用材质互相干扰
        if (_mat == null)
        {
            if (fillImage.material != null && fillImage.material.shader != null &&
                fillImage.material.shader.name == "MusicalSprite/UI/HPBarLiquid")
                _mat = new Material(fillImage.material);
            else
                _mat = new Material(sh);
            fillImage.material = _mat;
        }

        _mat.SetFloat(FlipID, side == 0 ? 0f : 1f);
        _mat.SetFloat(WaveFreqID, waveFreq);
        _mat.SetFloat(WaveSpeedID, waveSpeed);
        _mat.SetFloat(RippleScaleID, rippleScale);
        _mat.SetFloat(AmbientWaveID, ambientWave);
        _mat.SetColor(TopColorID, topColor);
        _mat.SetColor(BottomColorID, bottomColor);
        _mat.SetColor(CrestColorID, crestColor);
        _mat.SetFloat(EdgeSoftnessID, edgeSoftness);
        _mat.SetFloat(CrestWidthID, crestWidth);
        _mat.SetFloat(CrestIntensityID, crestIntensity);
        _mat.SetFloat(SurfaceGlowID, surfaceGlow);
        _mat.SetColor(SurfaceDarkenID, surfaceDarken);
        _mat.SetFloat(SurfaceDarkenRangeID, surfaceDarkenRange);
    }

    /// <summary>
    /// 外部直接设置血量比例（0~1）。比例下降会自动触发扣血特效。
    /// </summary>
    public void SetFill(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        float oldTarget = _targetFill;
        float delta = Mathf.Abs(ratio - oldTarget);

        if (delta > 1e-5f)
        {
            // 起点取"当前显示值"与"旧目标"的较大者，保证黄色残影覆盖完整
            _fillAnimStart = Mathf.Max(_currentFill, oldTarget);
            _fillAnimEnd = ratio;
            _fillAnimElapsed = 0f;
            _fillAnimating = true;

            // 只有扣血才触发摇晃与扣血特效；回血只播填充动画
            if (ratio < oldTarget - 1e-5f)
            {
                TriggerShake(oldTarget - ratio);
                if (enableDamageFx) StartDamageFx(oldTarget, ratio);
            }
        }

        _targetFill = ratio;
    }

    /// <summary>
    /// 无动画地直接设置血量（用于初始化 / 预览基线）。
    /// </summary>
    private void SetHPInstant(int hp, int max)
    {
        _targetFill = Mathf.Clamp01((float)hp / Mathf.Max(1, max));
        _currentFill = _targetFill;
        _fillAnimating = false;
        _shakeAmount = 0f;
        _shakeElapsed = shakeDuration;
        _fxGhostActive = false;
        if (_ghostMat != null) _ghostMat.SetFloat(FillID, 0f);
        if (_mat != null) _mat.SetFloat(FillID, _currentFill);
        if (flashImage != null) SetImageAlpha(flashImage, 0f);
        if (_rootRect != null) _rootRect.localScale = Vector3.one;

        if (hpNumberSprite != null)
        {
            hpNumberSprite.SetHP(hp);
            if (hpText != null) hpText.gameObject.SetActive(false);
        }
        else if (hpText != null)
        {
            hpText.text = $"HP {hp}";
        }
    }

    private void OnHPChanged(int leftHP, int rightHP)
    {
        int hp = side == 0 ? leftHP : rightHP;
        int max = scoreManager != null ? scoreManager.GetMaxHP(side) : 1;

        SetFill(Mathf.Clamp01((float)hp / Mathf.Max(1, max)));

        if (logHP)
        {
            Debug.Log($"[HPBarLiquid] '{name}' side={side} 血量变化 → {hp}/{max}（fill 目标 {_targetFill:F3}）");
        }

        if (hpNumberSprite != null)
        {
            hpNumberSprite.SetHP(hp);
            if (hpText != null) hpText.gameObject.SetActive(false);
        }
        else if (hpText != null)
        {
            hpText.text = $"HP {hp}";
        }
    }

    private void UpdateFillAnimation()
    {
        if (!_fillAnimating)
        {
            _currentFill = _targetFill;
            return;
        }

        _fillAnimElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_fillAnimElapsed / Mathf.Max(0.001f, fillDuration));

        float eased = 1f - Mathf.Pow(1f - t, fillEasePower);
        _currentFill = Mathf.Lerp(_fillAnimStart, _fillAnimEnd, eased);

        if (t >= 1f)
        {
            _currentFill = _fillAnimEnd;
            _fillAnimating = false;
        }
    }

    private void TriggerShake(float damageRatio)
    {
        float amount = impactWave + damageRatio * damageToShake;
        _shakeInitial = Mathf.Clamp(amount, minShake, waveMax);
        _shakeAmount = _shakeInitial;
        _shakeElapsed = 0f;
    }

    private void UpdateShake()
    {
        if (_shakeElapsed >= shakeDuration)
        {
            _shakeAmount = 0f;
            return;
        }

        _shakeElapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_shakeElapsed / Mathf.Max(0.001f, shakeDuration));

        float envelope = Mathf.Pow(1f - t, shakeEasePower);
        _shakeAmount = _shakeInitial * envelope;

        if (_shakeAmount < 0.0005f)
        {
            _shakeAmount = 0f;
            _shakeElapsed = shakeDuration;
        }
    }

    /// <summary>
    /// 触发一次完整扣血特效（闪白 + 放大 + 黄色残影）。
    /// fromFill/toFill 为受击前后的血量比例（0~1）。
    /// </summary>
    private void StartDamageFx(float fromFill, float toFill)
    {
        float dmg = Mathf.Clamp01((fromFill - toFill) / Mathf.Max(1e-4f, refDamageRatio));

        _fxFlashPeak = Mathf.Lerp(flashIntensityMin, flashIntensityMax, dmg);
        _fxFlashDur = flashDuration;
        _fxFlashElapsed = 0f;

        _fxPunchPeak = Mathf.Lerp(punchScaleMin, punchScaleMax, dmg);
        _fxPunchDur = punchDuration;
        _fxPunchElapsed = 0f;

        _fxGhostActive = true;
        _fxGhostStart = fromFill;   // 黄色残影顶部 = 受击前血量
        _fxGhostEnd = toFill;       // 底部 = 受击后血量
        _fxGhostElapsed = 0f;
        _fxGhostHold = ghostHold;
        _fxGhostDrain = ghostDrainDuration;
    }

    private void SetImageAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color;
        c.a = a;
        img.color = c;
    }

    // ===================== 编辑态预览 API =====================

    /// <summary>把血条瞬间设到某个血量比例（不播动画），作为预览基线。</summary>
    public void PreviewSetFill(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        EnsureMaterials();
        if (scoreManager != null) _maxHP = Mathf.Max(1, scoreManager.GetMaxHP(side));

        _targetFill = ratio;
        _currentFill = ratio;
        _fillAnimating = false;
        _shakeAmount = 0f;
        _shakeElapsed = shakeDuration;
        _fxGhostActive = false;
        if (_ghostMat != null) _ghostMat.SetFloat(FillID, 0f);
        if (_mat != null) _mat.SetFloat(FillID, ratio);
        if (flashImage != null) SetImageAlpha(flashImage, 0f);
        if (_rootRect != null) _rootRect.localScale = Vector3.one;

        int hp = Mathf.RoundToInt(ratio * _maxHP);
        if (hpNumberSprite != null)
        {
            hpNumberSprite.SetHP(hp);
            if (hpText != null) hpText.gameObject.SetActive(false);
        }
        else if (hpText != null)
        {
            hpText.text = $"HP {hp}";
        }
    }

    /// <summary>编辑态预览：从 fromRatio 扣血到 toRatio，并播放完整特效序列。</summary>
    public void PreviewDamage(float fromRatio, float toRatio)
    {
        fromRatio = Mathf.Clamp01(fromRatio);
        toRatio = Mathf.Clamp01(toRatio);
        if (scoreManager == null)
            scoreManager = FindFirstObjectByType<ScoreManager>();
        PreviewSetFill(fromRatio);

        _targetFill = toRatio;
        _fillAnimStart = fromRatio;
        _fillAnimEnd = toRatio;
        _fillAnimElapsed = 0f;
        _fillAnimating = true;

        if (toRatio < fromRatio - 1e-5f)
        {
            TriggerShake(fromRatio - toRatio);
            if (enableDamageFx) StartDamageFx(fromRatio, toRatio);
        }
    }

    /// <summary>特效序列是否已全部结束（用于预览窗口停止驱动）。</summary>
    public bool IsSettled()
    {
        return !_fillAnimating && _fxFlashElapsed >= _fxFlashDur &&
               _fxPunchElapsed >= _fxPunchDur && !_fxGhostActive;
    }
}
