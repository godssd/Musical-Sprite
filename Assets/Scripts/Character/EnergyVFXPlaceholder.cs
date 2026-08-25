using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 能量充满占位 VFX（P1 临时表现）。
/// - 当某队伍角色能量达到上限（currentEnergy >= maxEnergy），CharacterClass 触发 OnEnergyFull。
/// - 订阅角色事件：充满后从该角色身体位置持续冒出上浮小方块；施放技能能量低于上限触发 OnEnergyDepleted 后停止。
/// - 角色身体位置通过 anchors（characterId → Transform）指定；P1 无角色时不冒方块（inert）。
///
/// 挂载：场景任意 GO（FeverManager 会自动补建）。无正式美术，后续整体替换。
/// </summary>
public class EnergyVFXPlaceholder : MonoBehaviour
{
    [System.Serializable]
    public struct CharAnchor
    {
        public int characterId;
        public Transform body;
    }

    [Header("角色身体锚点（P2 把队伍方块 Transform 拖进来）")]
    public List<CharAnchor> anchors = new List<CharAnchor>();

    [Header("上浮小方块外观")]
    public float cubeSize = 0.2f;
    public Color cubeColor = Color.white;
    public float spawnInterval = 0.3f;
    public float riseSpeed = 0.8f;
    public float lifeTime = 1f;
    public float drift = 0.15f;

    private Dictionary<int, Transform> anchorMap = new Dictionary<int, Transform>();
    private readonly HashSet<int> activeIds = new HashSet<int>();
    private readonly Dictionary<int, float> spawnTimers = new Dictionary<int, float>();
    private readonly Dictionary<int, List<GameObject>> cubesByChar = new Dictionary<int, List<GameObject>>();

    void Start()
    {
        RebuildAnchorMap();
        CharacterRoster.OnRosterChanged += OnRosterChanged;
        Resubscribe();
    }

    void OnDestroy()
    {
        CharacterRoster.OnRosterChanged -= OnRosterChanged;
        UnsubscribeAll();
        ClearAllCubes();
    }

    private void RebuildAnchorMap()
    {
        anchorMap.Clear();
        foreach (var a in anchors)
            if (a.body != null) anchorMap[a.characterId] = a.body;
    }

    private void OnRosterChanged()
    {
        // 花名册变化（P2 注册角色）时重新订阅事件 + 自动填充锚点
        AutoFillAnchorsFromMarkers();
        Resubscribe();
    }

    private void AutoFillAnchorsFromMarkers()
    {
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        // 不能在 foreach anchors 过程中 Add，先收集待添加项
        var pending = new System.Collections.Generic.List<CharAnchor>();
        foreach (var m in markers)
        {
            if (m == null || m.IsPlayer) continue;
            if (m.GetComponent<Renderer>() == null) continue;
            // 关键：以 CharacterRoster 注册的 CharacterClass.characterId 为准，
            // 而不是用 (side+1)*100+lane+1 推算 —— 两者本来就不一致，
            // 会导致 activeIds.Add(realId) 后 anchorMap.TryGetValue(realId) 找不到。
            CharacterClass inst = CharacterRoster.GetTeam(m.side, m.laneIndex);
            if (inst == null) continue;
            int realId = inst.characterId;
            bool found = false;
            foreach (var a in anchors)
            {
                if (a.characterId == realId && a.body == m.transform) { found = true; break; }
            }
            if (!found)
                pending.Add(new CharAnchor { characterId = realId, body = m.transform });
        }
        anchors.AddRange(pending);
        RebuildAnchorMap();
    }

    private void Resubscribe()
    {
        UnsubscribeAll();
        foreach (var c in CharacterRoster.AllTeamCharacters())
        {
            c.OnEnergyFull += OnFull;
            c.OnEnergyDepleted += OnDepleted;
        }
    }

    private void UnsubscribeAll()
    {
        foreach (var c in CharacterRoster.AllTeamCharacters())
        {
            c.OnEnergyFull -= OnFull;
            c.OnEnergyDepleted -= OnDepleted;
        }
    }

    private void OnFull(int characterId)
    {
        activeIds.Add(characterId);
        if (!spawnTimers.ContainsKey(characterId)) spawnTimers[characterId] = 0f;
        if (!cubesByChar.ContainsKey(characterId)) cubesByChar[characterId] = new List<GameObject>();
    }

    private void OnDepleted(int characterId)
    {
        activeIds.Remove(characterId);
        ClearCharCubes(characterId);
    }

    void Update()
    {
        if (activeIds.Count == 0) return;
        foreach (var id in activeIds)
        {
            if (!anchorMap.TryGetValue(id, out var body) || body == null) continue;
            if (!cubesByChar.ContainsKey(id)) cubesByChar[id] = new List<GameObject>();
            spawnTimers.TryGetValue(id, out float t);
            t -= Time.deltaTime;
            if (t <= 0f)
            {
                t = spawnInterval;
                SpawnCube(body.position, id);
            }
            spawnTimers[id] = t;

            // 推进已有方块
            var list = cubesByChar[id];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var go = list[i];
                if (go == null) { list.RemoveAt(i); continue; }
                var mat = go.GetComponent<Renderer>().material;
                Color col = mat.color;
                col.a -= Time.deltaTime / lifeTime;
                mat.color = col;
                go.transform.position += Vector3.up * riseSpeed * Time.deltaTime;
                go.transform.position += new Vector3(
                    Mathf.Sin(Time.time * 3f + i) * drift * Time.deltaTime, 0f, 0f);
                if (col.a <= 0f)
                {
                    Destroy(go);
                    list.RemoveAt(i);
                }
            }
        }
    }

    private void SpawnCube(Vector3 origin, int id)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = origin + Vector3.up * 0.5f;
        cube.transform.localScale = Vector3.one * cubeSize;
        var rend = cube.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Standard"));
        rend.material.color = cubeColor;
        if (!cubesByChar.ContainsKey(id)) cubesByChar[id] = new List<GameObject>();
        cubesByChar[id].Add(cube);
    }

    private void ClearCharCubes(int characterId)
    {
        if (cubesByChar.TryGetValue(characterId, out var list))
        {
            foreach (var go in list) if (go != null) Destroy(go);
            list.Clear();
        }
    }

    private void ClearAllCubes()
    {
        foreach (var kv in cubesByChar)
            foreach (var go in kv.Value) if (go != null) Destroy(go);
        cubesByChar.Clear();
    }

    /// <summary>由 CharacterBattleSystem 在自动补完 marker 后调用，确保锚点与角色对应。</summary>
    public void RebuildFromRosterAndMarkers()
    {
        // 清除掉旧版本写入的 (side+1)*100+laneIndex+1 形式假 ID 锚点，防止历史脏数据卡死事件
        for (int i = anchors.Count - 1; i >= 0; i--)
        {
            int id = anchors[i].characterId;
            // 历史公式产出的 ID 都在 100..299 范围；合法的 CharacterClass.characterId 是 1..5（默认）。
            if (id >= 100) anchors.RemoveAt(i);
        }
        AutoFillAnchorsFromMarkers();
        Resubscribe();
    }
}
