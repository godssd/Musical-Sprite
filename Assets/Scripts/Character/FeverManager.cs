using UnityEngine;
using System;

/// <summary>
/// 过热（Fever / Super Fever）运行时管理器。
/// - 订阅 NoteSpawner.OnJudge 维护每 side 连击；非 MISS 累加、MISS 清零并退出过热。
/// - 根据连击阈值切换 FeverState，并通过 OnStateChanged 事件广播（供 VFX 订阅）。
/// - GetScoreMultiplier(side) 每次从 FeverConfigSO 实时读取系数，MS Debug 改值即时生效。
/// - 缺失配置时自动创建默认 FeverConfig.asset（仅编辑器）或运行时实例。
///
/// 挂载：场景任意 GameObject（ScoreManager 会自动补建，无需手动拖）。
/// </summary>
public class FeverManager : MonoBehaviour
{
    [Header("配置")]
    public FeverConfigSO config;

    private int[] combo = new int[2];
    private FeverState[] state = new FeverState[2];

    public event Action<FeverState, FeverState, int> OnStateChanged; // (old, new, side)

    void Awake()
    {
        EnsureConfig();
    }

    void Start()
    {
        NoteSpawner[] spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        if (spawners.Length == 0)
            Debug.LogWarning("[FeverManager] 未找到 NoteSpawner，过热连击不工作");
        foreach (var s in spawners)
            s.OnJudge += HandleJudge;

        EnsureFeverVFX();
        EnsureFeverBanner();
        EnsureEnergyVFX();
    }

    void OnDestroy()
    {
        NoteSpawner[] spawners = FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
        foreach (var s in spawners)
            s.OnJudge -= HandleJudge;
    }

    private void HandleJudge(int side, int lane, string rank, Vector3 pos, UnityEngine.Object source)
    {
        if (rank == "MISS")
            OnNoteMiss(side);
        else
            OnNoteHit(side);
    }

    public void OnNoteHit(int side)
    {
        if (side < 0 || side > 1) return;
        combo[side]++;
        Recalc(side);
    }

    public void OnNoteMiss(int side)
    {
        if (side < 0 || side > 1) return;
        if (combo[side] == 0 && state[side] == FeverState.None) return;
        combo[side] = 0;
        FeverState old = state[side];
        state[side] = FeverState.None;
        if (old != FeverState.None)
            OnStateChanged?.Invoke(old, FeverState.None, side);
    }

    private void Recalc(int side)
    {
        if (config == null) return;
        FeverState ns = FeverState.None;
        if (combo[side] >= config.superFeverComboThreshold) ns = FeverState.SuperFever;
        else if (combo[side] >= config.feverComboThreshold) ns = FeverState.Fever;

        if (ns != state[side])
        {
            FeverState old = state[side];
            state[side] = ns;
            OnStateChanged?.Invoke(old, ns, side);
        }
    }

    /// <summary>该 side 当前得分乘数（实时从 config 读，MS Debug 改值立即生效）。</summary>
    public float GetScoreMultiplier(int side)
    {
        if (config == null) return 1f;
        switch (state[side])
        {
            case FeverState.Fever: return config.feverScoreMultiplier;
            case FeverState.SuperFever: return config.superFeverScoreMultiplier;
            default: return 1f;
        }
    }

    public FeverState GetState(int side) => state[side];
    public int GetCombo(int side) => combo[side];

    private void EnsureConfig()
    {
        if (config != null) return;
#if UNITY_EDITOR
        string path = "Assets/Data/FeverConfig.asset";
        config = UnityEditor.AssetDatabase.LoadAssetAtPath<FeverConfigSO>(path);
        if (config == null)
        {
            if (!System.IO.Directory.Exists("Assets/Data"))
                System.IO.Directory.CreateDirectory("Assets/Data");
            config = ScriptableObject.CreateInstance<FeverConfigSO>();
            UnityEditor.AssetDatabase.CreateAsset(config, path);
            UnityEditor.AssetDatabase.SaveAssets();
            Debug.Log("[FeverManager] 已创建默认配置 " + path);
        }
#else
        config = ScriptableObject.CreateInstance<FeverConfigSO>();
#endif
    }

    private void EnsureFeverVFX()
    {
        if (FindFirstObjectByType<FeverVFXPlaceholder>() != null) return;
        var go = new GameObject("FeverVFXPlaceholder");
        go.AddComponent<FeverVFXPlaceholder>();
    }

    private void EnsureFeverBanner()
    {
        // 每侧一座山峦涂鸦：side=0 在场地左中线上空，side=1 在右中线上空
        var existing = FindObjectsByType<FeverBanner>(FindObjectsSortMode.None);
        for (int s = 0; s < 2; s++)
        {
            bool sideExists = false;
            foreach (var b in existing) { if (b != null && b.side == s) { sideExists = true; break; } }
            if (sideExists) continue;
            var go = new GameObject(s == 0 ? "FeverBanner_Left" : "FeverBanner_Right");
            var banner = go.AddComponent<FeverBanner>();
            banner.side = s;
            banner.sideOffsetX = s == 0 ? -2.4f : 2.4f;
        }
    }

    private void EnsureEnergyVFX()
    {
        if (FindFirstObjectByType<EnergyVFXPlaceholder>() != null) return;
        var go = new GameObject("EnergyVFXPlaceholder");
        go.AddComponent<EnergyVFXPlaceholder>();
    }
}
