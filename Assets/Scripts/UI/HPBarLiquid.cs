using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 液体血条控制器（配合 Shader "MusicalSprite/UI/HPBarLiquid" 使用）。
///
/// 与旧版 HPBarDisplay 的区别：
/// 旧版用 RectTransform 锚点缩放来控制宽度，那样会把波纹一起压扁，做不出液体感。
/// 本脚本让 Fill 保持满尺寸，改由 Shader 的 _Fill 参数决定液面位置，
/// 波纹才能正常起伏。
///
/// 扣血动画：
///   - 血量下降使用先快后慢的 ease-out 曲线，总时长固定。
///   - 伤害越大，单位时间内需要移动的距离越大，因此起始速度自然更快。
///   - 同时触发液面摇晃，摇晃幅度与伤害量挂钩，并先快后慢地衰减至平静。
///
/// 层级要求：
///   HPBarRoot（挂本脚本）
///     ├── Background  (Image, Sprite = HPBar_Container.png, Color = #000000)
///     ├── Fill        (Image, Sprite = HPBar_Container.png, Material = M_HPBarLiquid)
///     └── Frame       (Image, Sprite = HPBar_Frame.png, 放在最上层)
///
/// side：0 = 左玩家（红，液体靠左、右侧是液面）；1 = 右玩家（蓝，镜像，左侧是液面）
/// </summary>
public class HPBarLiquid : MonoBehaviour
{
    [Header("归属")]
    [Tooltip("0 = 左玩家（红），1 = 右玩家（蓝）")]
    public int side = 0;

    [Header("引用")]
    public ScoreManager scoreManager;
    public Image fillImage;
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

    [Header("摇晃（扣血时触发）")]
    [Tooltip("扣血基础摇晃幅度")]
    public float impactWave = 0.03f;
    [Tooltip("伤害比例转摇晃幅度的系数（例如 0.2 伤害 => 0.2 * 0.3 = 0.06 幅度）")]
    public float damageToShake = 0.35f;
    [Tooltip("最小摇晃幅度，避免小伤害没反应")]
    public float minShake = 0.025f;
    [Tooltip("摇晃幅度上限")]
    public float waveMax = 0.14f;
    [Tooltip("摇晃总时长（秒）")]
    public float shakeDuration = 0.55f;
    [Tooltip("摇晃衰减指数，越大开头衰减越快、后段越慢（建议 2~3）")]
    public float shakeEasePower = 2.2f;
    [Tooltip("波纹频率（纵向密度）")]
    public float waveFreq = 14f;
    [Tooltip("波纹流动速度")]
    public float waveSpeed = 5f;
    [Tooltip("环境微波动幅度（无扣血时的轻微液面起伏）")]
    [Range(0f, 0.03f)] public float ambientWave = 0.010f;

    [Header("液面形状")]
    [Range(0.001f, 0.05f)] public float edgeSoftness = 0.004f;
    [Range(0f, 0.3f)] public float crestWidth = 0.08f;
    [Range(0f, 3f)] public float crestIntensity = 1.0f;
    [Range(0f, 2f)] public float surfaceGlow = 0.4f;
    [Tooltip("纯色模式下请保持与液体颜色相同")]
    public Color surfaceDarken = new Color(1.00f, 0.15f, 0.00f);
    [Tooltip("纯色模式下建议 0")]
    [Range(0f, 0.5f)] public float surfaceDarkenRange = 0.0f;

    [Header("调试")]
    [Tooltip("开启后会在 Console 打印血量变化，方便排查“不掉血”问题。确认正常后可关掉。")]
    public bool logHP = true;

    private Material _mat;
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

    private static readonly int FillID           = Shader.PropertyToID("_Fill");
    private static readonly int FlipID           = Shader.PropertyToID("_Flip");
    private static readonly int WaveAmpID        = Shader.PropertyToID("_WaveAmp");
    private static readonly int WaveFreqID       = Shader.PropertyToID("_WaveFreq");
    private static readonly int WaveSpeedID      = Shader.PropertyToID("_WaveSpeed");
    private static readonly int AmbientWaveID    = Shader.PropertyToID("_AmbientWave");
    private static readonly int TopColorID       = Shader.PropertyToID("_TopColor");
    private static readonly int BottomColorID    = Shader.PropertyToID("_BottomColor");
    private static readonly int CrestColorID     = Shader.PropertyToID("_CrestColor");
    private static readonly int EdgeSoftnessID   = Shader.PropertyToID("_EdgeSoftness");
    private static readonly int CrestWidthID     = Shader.PropertyToID("_CrestWidth");
    private static readonly int CrestIntensityID    = Shader.PropertyToID("_CrestIntensity");
    private static readonly int SurfaceGlowID       = Shader.PropertyToID("_SurfaceGlow");
    private static readonly int SurfaceDarkenID     = Shader.PropertyToID("_SurfaceDarken");
    private static readonly int SurfaceDarkenRangeID = Shader.PropertyToID("_SurfaceDarkenRange");

    void Start()
    {
        if (scoreManager == null)
            scoreManager = FindFirstObjectByType<ScoreManager>();

        ResolveFillImage();
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
        if (_mat == null)
        {
            SetupMaterial();
            if (_mat == null) return;
        }

        UpdateFillAnimation();
        UpdateShake();

        _mat.SetFloat(FillID, _currentFill);
        _mat.SetFloat(WaveAmpID, _shakeAmount);
        _mat.SetFloat(AmbientWaveID, ambientWave);
    }

    void OnDestroy()
    {
        if (scoreManager != null)
            scoreManager.OnHPChanged -= OnHPChanged;

        if (_mat != null)
        {
            Destroy(_mat);
            _mat = null;
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
            // 确保 Fill 渲染在 Background 之上
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

        // 运行时实例化，避免左右两条血条共用材质互相干扰
        if (_mat == null)
        {
            _mat = new Material(sh);
            fillImage.material = _mat;
        }

        _mat.SetFloat(FlipID, side == 0 ? 0f : 1f);
        _mat.SetFloat(WaveFreqID, waveFreq);
        _mat.SetFloat(WaveSpeedID, waveSpeed);
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
    /// 外部直接设置血量比例（0~1）。比例下降会自动触发波动。
    /// </summary>
    public void SetFill(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        float delta = Mathf.Abs(ratio - _targetFill);

        if (delta > 1e-5f)
        {
            // 启动填充动画：从当前视觉血量开始，总时长固定
            _fillAnimStart = _currentFill;
            _fillAnimEnd = ratio;
            _fillAnimElapsed = 0f;
            _fillAnimating = true;

            // 只有扣血才摇晃；回血只播填充动画
            if (ratio < _targetFill - 1e-5f)
            {
                TriggerShake(_targetFill - ratio);
            }
        }

        _targetFill = ratio;
    }

    /// <summary>
    /// 无动画地直接设置血量（用于初始化）。
    /// </summary>
    private void SetHPInstant(int hp, int max)
    {
        _targetFill = Mathf.Clamp01((float)hp / Mathf.Max(1, max));
        _currentFill = _targetFill;
        _fillAnimating = false;
        _shakeAmount = 0f;
        _shakeElapsed = shakeDuration;

        if (_mat != null) _mat.SetFloat(FillID, _currentFill);

        // 有图片数字时用它，隐藏 Text 避免重影
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

        // ease-out：先快后慢；伤害越大，相同时长内需移动距离越大，起始速度越快
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
        // 摇晃幅度与伤害量挂钩，但有下限和上限
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

        // 先快后慢的衰减包络：t=0 时 1，t=0.5 时已大幅衰减，之后缓慢归零
        float envelope = Mathf.Pow(1f - t, shakeEasePower);
        _shakeAmount = _shakeInitial * envelope;

        if (_shakeAmount < 0.0005f)
        {
            _shakeAmount = 0f;
            _shakeElapsed = shakeDuration;
        }
    }
}
