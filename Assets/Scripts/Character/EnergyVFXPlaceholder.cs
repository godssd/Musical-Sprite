using UnityEngine;
using System;
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

    // 左右两侧会共享 characterId，必须按 CharacterClass 实例区分，否则同 ID 只能保留一个冒烟锚点。
    private readonly Dictionary<CharacterClass, Transform> anchorMap = new Dictionary<CharacterClass, Transform>();
    private readonly HashSet<CharacterClass> activeCharacters = new HashSet<CharacterClass>();
    private readonly Dictionary<CharacterClass, float> spawnTimers = new Dictionary<CharacterClass, float>();
    private readonly Dictionary<CharacterClass, List<GameObject>> cubesByChar = new Dictionary<CharacterClass, List<GameObject>>();
    private readonly Dictionary<CharacterClass, Action<int, int>> fullHandlers = new Dictionary<CharacterClass, Action<int, int>>();
    private readonly Dictionary<CharacterClass, Action<int, int>> depletedHandlers = new Dictionary<CharacterClass, Action<int, int>>();

    void Start()
    {
        AutoFillAnchorsFromMarkers();
        CharacterRoster.OnRosterChanged += OnRosterChanged;
        Resubscribe();
    }

    void OnDestroy()
    {
        CharacterRoster.OnRosterChanged -= OnRosterChanged;
        UnsubscribeAll();
        ClearAllCubes();
    }

    private void OnRosterChanged()
    {
        // 花名册变化（P2 注册角色）时重新订阅事件 + 自动填充锚点
        AutoFillAnchorsFromMarkers();
        Resubscribe();
    }

    private void AutoFillAnchorsFromMarkers()
    {
        anchors.Clear();
        anchorMap.Clear();
        var markers = FindObjectsByType<CharacterCubeMarker>(FindObjectsSortMode.None);
        foreach (var m in markers)
        {
            if (m == null || m.IsPlayer) continue;
            if (m.GetComponent<Renderer>() == null) continue;
            CharacterClass inst = CharacterRoster.GetTeam(m.side, m.laneIndex);
            if (inst == null) continue;
            anchorMap[inst] = m.transform;
            anchors.Add(new CharAnchor { characterId = inst.characterId, body = m.transform });
        }
    }

    private void Resubscribe()
    {
        UnsubscribeAll();
        activeCharacters.Clear();
        spawnTimers.Clear();
        ClearAllCubes();
        foreach (var c in CharacterRoster.AllTeamCharacters())
        {
            CharacterClass character = c;
            Action<int, int> onFull = (_, __) => OnFull(character);
            Action<int, int> onDepleted = (_, __) => OnDepleted(character);
            fullHandlers[character] = onFull;
            depletedHandlers[character] = onDepleted;
            character.OnEnergyFull += onFull;
            character.OnEnergyDepleted += onDepleted;

            // 订阅发生在充满事件之后时也要补上表现，保证“只要当前充能满就冒烟”（多主动槽任一满即冒烟）。
            bool anyFull = false;
            if (character.maxEnergies != null)
                for (int i = 0; i < character.maxEnergies.Length; i++)
                    if (character.maxEnergies[i] > 0f && character.currentEnergies[i] >= character.maxEnergies[i] && !character.skillBusyArr[i])
                    { anyFull = true; break; }
            if (anyFull) OnFull(character);
        }
    }

    private void UnsubscribeAll()
    {
        foreach (var pair in fullHandlers) pair.Key.OnEnergyFull -= pair.Value;
        foreach (var pair in depletedHandlers) pair.Key.OnEnergyDepleted -= pair.Value;
        fullHandlers.Clear();
        depletedHandlers.Clear();
    }

    private void OnFull(CharacterClass character)
    {
        if (character == null) return;
        activeCharacters.Add(character);
        if (!spawnTimers.ContainsKey(character)) spawnTimers[character] = 0f;
        if (!cubesByChar.ContainsKey(character)) cubesByChar[character] = new List<GameObject>();
    }

    private void OnDepleted(CharacterClass character)
    {
        if (character == null) return;
        activeCharacters.Remove(character);
        ClearCharCubes(character);
    }

    void Update()
    {
        if (activeCharacters.Count == 0) return;
        foreach (var character in activeCharacters)
        {
            if (!anchorMap.TryGetValue(character, out var body) || body == null) continue;
            if (!cubesByChar.ContainsKey(character)) cubesByChar[character] = new List<GameObject>();
            spawnTimers.TryGetValue(character, out float t);
            t -= Time.deltaTime;
            if (t <= 0f)
            {
                t = spawnInterval;
                SpawnCube(body.position, character);
            }
            spawnTimers[character] = t;

            // 推进已有方块
            var list = cubesByChar[character];
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

    private void SpawnCube(Vector3 origin, CharacterClass character)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position = origin + Vector3.up * 0.5f;
        cube.transform.localScale = Vector3.one * cubeSize;
        var rend = cube.GetComponent<Renderer>();
        rend.material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        rend.material.color = cubeColor;
        if (!cubesByChar.ContainsKey(character)) cubesByChar[character] = new List<GameObject>();
        cubesByChar[character].Add(cube);
    }

    private void ClearCharCubes(CharacterClass character)
    {
        if (cubesByChar.TryGetValue(character, out var list))
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
        AutoFillAnchorsFromMarkers();
        Resubscribe();
    }
}
