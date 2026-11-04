#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 血条扣血特效 · 编辑态预览窗口。
///
/// 选中一条 HPBarLiquid（Hierarchy 里点 HPBarLeft / HPBarRight），
/// 设好起始/目标血量，点"播放扣血"，即可在 Scene 视图直接看到
/// 闪白 + 放大 + 黄色残影 + 红色下落 的完整序列，无需进入 Play。
///
/// 入口：Unity 菜单 → Tools → Musical-Sprite → HP Bar FX Preview
///
/// 调参流程：
///   1. 在 Scene 里看效果 → 2. 在 Inspector 改 HPBarLiquid 上的"扣血特效"参数
///      （闪白时长/强度、放大峰值、黄色残影保持/锐减、伤害归一 refDamageRatio 等）
///      → 3. 再点播放对比 → 4. 满意后点"复制当前特效参数"把数值发给我固化。
/// </summary>
public class HPBarFxPreviewWindow : EditorWindow
{
    private HPBarLiquid _target;
    private float _from = 1f;
    private float _to = 0.5f;
    private double _lastTime;
    private bool _playing;

    [MenuItem("Tools/Musical-Sprite/HP Bar FX Preview")]
    static void Open()
    {
        var w = GetWindow<HPBarFxPreviewWindow>();
        w.titleContent = new GUIContent("HP Bar FX");
        w.minSize = new Vector2(280, 220);
    }

    void OnEnable()
    {
        if (Selection.activeGameObject != null)
            _target = Selection.activeGameObject.GetComponent<HPBarLiquid>();
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("血条扣血特效 · 编辑态预览", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "选中一条 HPBarLiquid（Hierarchy 点 HPBarLeft/Right），设好起始/目标血量，点'播放扣血'即可在 Scene 视图看到完整扣血特效，无需进入 Play。",
            MessageType.Info);

        _target = (HPBarLiquid)EditorGUILayout.ObjectField("目标血条", _target, typeof(HPBarLiquid), true);

        _from = EditorGUILayout.Slider("起始血量 %", _from * 100f, 0f, 100f) / 100f;
        _to = EditorGUILayout.Slider("目标血量 %", _to * 100f, 0f, 100f) / 100f;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("▶ 播放扣血") && _target != null) StartPreview();
        if (GUILayout.Button("⏹ 停止")) StopPreview();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("设定基线（不播放）") && _target != null)
            _target.PreviewSetFill(_from);

        if (GUILayout.Button("📋 复制当前特效参数") && _target != null)
            CopyConfig();

        if (_playing)
            EditorGUILayout.LabelField("预览播放中…（看 Scene 视图）", EditorStyles.helpBox);
    }

    void StartPreview()
    {
        if (_target == null) return;
        StopPreview();
        _target.PreviewDamage(_from, _to);
        _playing = true;
        _lastTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    void StopPreview()
    {
        _playing = false;
        EditorApplication.update -= Tick;
    }

    void Tick()
    {
        if (_target == null) { StopPreview(); return; }

        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - _lastTime);
        _lastTime = now;
        if (dt > 0.1f) dt = 0.1f; // 防止切回编辑器时一次跳太大

        _target.TickFX(dt);
        SceneView.RepaintAll();

        if (_target.IsSettled())
            StopPreview();
    }

    void CopyConfig()
    {
        var t = _target;
        if (t == null) return;
        string s =
            $"// HPBarLiquid 特效参数（side={t.side}）\n" +
            $"enableDamageFx = {t.enableDamageFx};\n" +
            $"ghostColor = {t.ghostColor};\n" +
            $"flashDuration = {t.flashDuration}f;\n" +
            $"flashIntensityMin = {t.flashIntensityMin}f;\n" +
            $"flashIntensityMax = {t.flashIntensityMax}f;\n" +
            $"punchScaleMin = {t.punchScaleMin}f;\n" +
            $"punchScaleMax = {t.punchScaleMax}f;\n" +
            $"punchDuration = {t.punchDuration}f;\n" +
            $"ghostHold = {t.ghostHold}f;\n" +
            $"ghostDrainDuration = {t.ghostDrainDuration}f;\n" +
            $"refDamageRatio = {t.refDamageRatio}f;\n" +
            $"rippleScale = {t.rippleScale}f;\n" +
            $"impactWave = {t.impactWave}f;\n" +
            $"damageToShake = {t.damageToShake}f;\n" +
            $"fillDuration = {t.fillDuration}f;\n";
        EditorGUIUtility.systemCopyBuffer = s;
        Debug.Log("[HPBarFxPreview] 已复制特效参数到剪贴板。\n" + s);
    }

    void OnDestroy()
    {
        StopPreview();
    }
}
#endif
