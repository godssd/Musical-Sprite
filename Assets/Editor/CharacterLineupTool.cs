#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// 阵容测试工具：非运行/运行状态均可切换红蓝双方上场角色与位置，并固化每个槽位的位姿。
/// 玩家主位只能放 isPlayer=true 的角色；台下 4 个 Aibo 位只能放 isPlayer=false 的角色。
/// 每个槽位独立选择，支持同角色多登场测试。
/// </summary>
public class CharacterLineupTool : EditorWindow
{
    [System.Serializable]
    private class SlotConfig
    {
        [SerializeField] public string label;
        [SerializeField] public string sceneObjectName;
        [SerializeField] public bool isPlayerSlot;
        [SerializeField] public int side;           // 0=左/红, 1=右/蓝
        [SerializeField] public int laneIndex;      // -1=玩家, 0..3=Aibo
        [SerializeField] public CharacterDataSO character;
    }

    [SerializeField] private SlotConfig[] slots;
    private CharacterDataSO[] allChars;
    private Vector2 scroll;
    private string statusMsg = "";

    private const string EditorPrefsKey = "MusicalSprite_CharacterLineupTool_v2";

    [MenuItem("Tools/Musical Sprite/Character Lineup Tool")]
    static void Open() => GetWindow<CharacterLineupTool>("阵容工具");

    private void OnEnable()
    {
        // 只在首次打开时初始化槽位；后续由 Unity 自动反序列化保存选择。
        if (slots == null || slots.Length == 0) InitSlots();
        RefreshCharacterList();
        RestoreFromEditorPrefs();
    }

    private void OnDisable()
    {
        SaveToEditorPrefs();
    }

    private void SaveToEditorPrefs()
    {
        if (slots == null) return;
        var ids = slots.Select(s => s.character == null ? "0" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(s.character))).ToArray();
        EditorPrefs.SetString(EditorPrefsKey, string.Join(",", ids));
    }

    private void RestoreFromEditorPrefs()
    {
        if (slots == null) return;
        var saved = EditorPrefs.GetString(EditorPrefsKey, "");
        if (string.IsNullOrEmpty(saved)) return;
        var guids = saved.Split(',');
        for (int i = 0; i < slots.Length && i < guids.Length; i++)
        {
            var g = guids[i];
            if (string.IsNullOrEmpty(g) || g == "0") continue;
            var path = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrEmpty(path)) continue;
            slots[i].character = AssetDatabase.LoadAssetAtPath<CharacterDataSO>(path);
        }
    }

    private void InitSlots()
    {
        slots = new SlotConfig[10];
        int i = 0;
        slots[i++] = new SlotConfig { label = "红方-玩家（台子）", sceneObjectName = "Cube", isPlayerSlot = true, side = 0, laneIndex = -1 };
        slots[i++] = new SlotConfig { label = "红方-Aibo 0", sceneObjectName = "LeftBand_Member_Lane0", isPlayerSlot = false, side = 0, laneIndex = 0 };
        slots[i++] = new SlotConfig { label = "红方-Aibo 1", sceneObjectName = "LeftBand_Member_Lane1", isPlayerSlot = false, side = 0, laneIndex = 1 };
        slots[i++] = new SlotConfig { label = "红方-Aibo 2", sceneObjectName = "LeftBand_Member_Lane2", isPlayerSlot = false, side = 0, laneIndex = 2 };
        slots[i++] = new SlotConfig { label = "红方-Aibo 3", sceneObjectName = "LeftBand_Member_Lane3", isPlayerSlot = false, side = 0, laneIndex = 3 };
        slots[i++] = new SlotConfig { label = "蓝方-玩家（台子）", sceneObjectName = "", isPlayerSlot = true, side = 1, laneIndex = -1 };
        slots[i++] = new SlotConfig { label = "蓝方-Aibo 0", sceneObjectName = "RightBand_Member_Lane0", isPlayerSlot = false, side = 1, laneIndex = 0 };
        slots[i++] = new SlotConfig { label = "蓝方-Aibo 1", sceneObjectName = "RightBand_Member_Lane1", isPlayerSlot = false, side = 1, laneIndex = 1 };
        slots[i++] = new SlotConfig { label = "蓝方-Aibo 2", sceneObjectName = "RightBand_Member_Lane2", isPlayerSlot = false, side = 1, laneIndex = 2 };
        slots[i++] = new SlotConfig { label = "蓝方-Aibo 3", sceneObjectName = "RightBand_Member_Lane3", isPlayerSlot = false, side = 1, laneIndex = 3 };
    }

    private void RefreshCharacterList()
    {
        allChars = AssetDatabase.FindAssets("t:CharacterDataSO")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<CharacterDataSO>)
            .Where(x => x != null)
            .OrderBy(x => x.characterId)
            .ToArray();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("角色阵容测试工具", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("玩家位只能放 isPlayer=true 的角色；Aibo 位只能放 isPlayer=false 的角色。", EditorStyles.wordWrappedLabel);
        EditorGUILayout.Space(5);

        if (GUILayout.Button("刷新角色列表")) RefreshCharacterList();
        EditorGUILayout.LabelField($"已加载 CharacterDataSO: {allChars?.Length ?? 0}", EditorStyles.miniLabel);

        EditorGUILayout.Space(5);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("确保所有槽位有 CharacterSlotPose")) EnsureSlotPoses();
            if (GUILayout.Button("应用阵容到 CharacterBattleSystem")) ApplyLineup();
        }
        EditorGUILayout.Space(3);
        if (GUILayout.Button("同步红蓝 Aibo 位姿（红→蓝）")) SyncSlotPosesRedToBlue();
        EditorGUILayout.LabelField("微调说明：调好红方 Aibo 位姿后点此按钮，可把同一 lane 的 offset 复制到蓝方。", EditorStyles.miniLabel);

        EditorGUILayout.Space(10);
        scroll = EditorGUILayout.BeginScrollView(scroll);

        for (int i = 0; i < slots.Length; i++)
        {
            var s = slots[i];
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField(s.label, EditorStyles.boldLabel);

            var filtered = allChars.Where(c => c != null && c.isPlayer == s.isPlayerSlot).ToArray();
            var names = filtered.Select(c => $"{c.characterId}. {c.displayName}").Prepend("(无)").ToArray();
            int selected = s.character == null ? 0 : System.Array.IndexOf(filtered, s.character) + 1;
            int next = EditorGUILayout.Popup("角色", selected, names);
            s.character = next <= 0 ? null : filtered[next - 1];

            if (s.character != null)
            {
                EditorGUILayout.LabelField($"animationPrefix: {s.character.animationPrefix}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3);
        }

        EditorGUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(statusMsg))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(statusMsg, MessageType.Info);
        }
    }

    private CharacterBattleSystem FindOrCreateBattleSystem()
    {
        var sys = FindFirstObjectByType<CharacterBattleSystem>();
        if (sys != null) return sys;

        var go = GameObject.Find("CharacterBattleSystem");
        if (go != null) return go.GetComponent<CharacterBattleSystem>();

        // 场景里没有：自动创建一个空物体并挂组件，确保工具能写入且 ScoreManager 会复用它。
        go = new GameObject("CharacterBattleSystem");
        sys = go.AddComponent<CharacterBattleSystem>();
        Undo.RegisterCreatedObjectUndo(go, "Create CharacterBattleSystem");
        EditorUtility.SetDirty(go);
        return sys;
    }

    private void ApplyLineup()
    {
        var sys = FindOrCreateBattleSystem();
        if (sys == null)
        {
            statusMsg = "无法创建或找到 CharacterBattleSystem。";
            return;
        }

        var so = new SerializedObject(sys);
        var leftProp = so.FindProperty("leftCharacters");
        var rightProp = so.FindProperty("rightCharacters");
        if (leftProp == null || rightProp == null)
        {
            statusMsg = "CharacterBattleSystem 上找不到 leftCharacters / rightCharacters 字段。";
            return;
        }

        // 按槽位顺序写入：索引 0=玩家，1~4=lane0~3
        WriteLineupArray(leftProp, slots.Where(s => s.side == 0).OrderBy(s => s.laneIndex).ToArray());
        WriteLineupArray(rightProp, slots.Where(s => s.side == 1).OrderBy(s => s.laneIndex).ToArray());
        so.ApplyModifiedProperties();

        // 清空旧版 allCharacters，避免冲突。
        var allProp = so.FindProperty("allCharacters");
        if (allProp != null && allProp.arraySize > 0)
        {
            allProp.arraySize = 0;
            so.ApplyModifiedProperties();
        }

        EditorUtility.SetDirty(sys);
        EditorUtility.SetDirty(sys.gameObject);
        AssetDatabase.SaveAssets();

        // 非运行状态下应用阵容时，自动 Save 当前场景，确保 CharacterBattleSystem 预置物持久化。
        if (!Application.isPlaying)
        {
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        }

        int leftCount = slots.Count(s => s.side == 0 && s.character != null);
        int rightCount = slots.Count(s => s.side == 1 && s.character != null);
        statusMsg = $"已应用阵容：红方 {leftCount} 人，蓝方 {rightCount} 人。索引 0=玩家，1~4=lane0~3。{(Application.isPlaying ? "运行中：需重开 Play 才能按新阵容装载。" : "非运行：下次 Play 生效。")}";
    }

    private void WriteLineupArray(SerializedProperty prop, SlotConfig[] sideSlots)
    {
        // 确保 5 个槽位：0=玩家，1~4=lane0~3
        var ordered = sideSlots.OrderBy(s => s.laneIndex).ToArray();
        prop.arraySize = 5;
        for (int i = 0; i < 5; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = ordered[i].character;
        }
    }

    private void WriteArray(SerializedProperty prop, List<CharacterDataSO> list)
    {
        prop.arraySize = list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            prop.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
        }
    }

    private void EnsureSlotPoses()
    {
        int added = 0;
        foreach (var s in slots)
        {
            if (string.IsNullOrEmpty(s.sceneObjectName)) continue;
            var go = GameObject.Find(s.sceneObjectName);
            if (go == null) continue;
            if (go.GetComponent<CharacterSlotPose>() == null)
            {
                Undo.AddComponent<CharacterSlotPose>(go);
                added++;
                EditorUtility.SetDirty(go);
            }
            // 同时确保槽位上的 CharacterCubeMarker 存在，并把当前 SlotPose 同步为 fallback。
            var marker = go.GetComponent<CharacterCubeMarker>();
            if (marker == null)
            {
                marker = Undo.AddComponent<CharacterCubeMarker>(go);
                marker.side = s.side;
                marker.laneIndex = s.isPlayerSlot ? -1 : s.laneIndex;
                EditorUtility.SetDirty(marker);
            }
            SyncSlotPoseToMarkerFallback(go);
        }
        if (added > 0) AssetDatabase.SaveAssets();
        statusMsg = $"已为 {added} 个槽位物体添加 CharacterSlotPose，并同步 fallback 位姿。";
    }

    /// <summary>把 GameObject 上 CharacterSlotPose 的当前值复制到同物体 CharacterCubeMarker 的 fallback 字段。
    /// 这样即使运行时 SlotPose 读取异常，Marker 自身仍保有正确的位姿数据。</summary>
    private void SyncSlotPoseToMarkerFallback(GameObject go)
    {
        var pose = go.GetComponent<CharacterSlotPose>();
        var marker = go.GetComponent<CharacterCubeMarker>();
        if (pose == null || marker == null) return;
        Undo.RecordObject(marker, "Sync SlotPose to Marker Fallback");
        marker.fallbackPositionOffset = pose.positionOffset;
        marker.fallbackRotationOffset = pose.rotationOffset;
        marker.fallbackScaleOffset = pose.scaleOffset;
        EditorUtility.SetDirty(marker);
    }

    private void SyncSlotPosesRedToBlue()
    {
        int synced = 0;
        var pairs = new (string red, string blue)[]
        {
            ("LeftBand_Member_Lane0", "RightBand_Member_Lane0"),
            ("LeftBand_Member_Lane1", "RightBand_Member_Lane1"),
            ("LeftBand_Member_Lane2", "RightBand_Member_Lane2"),
            ("LeftBand_Member_Lane3", "RightBand_Member_Lane3"),
        };

        foreach (var (redName, blueName) in pairs)
        {
            var redGo = GameObject.Find(redName);
            var blueGo = GameObject.Find(blueName);
            if (redGo == null || blueGo == null) continue;

            var redPose = redGo.GetComponent<CharacterSlotPose>();
            if (redPose == null) continue;

            var bluePose = blueGo.GetComponent<CharacterSlotPose>();
            if (bluePose == null) bluePose = Undo.AddComponent<CharacterSlotPose>(blueGo);

            Undo.RecordObject(bluePose, "Sync Slot Pose Red to Blue");
            // 镜像规则（左右对称）：
            // 1) position.x 取反（左右镜像位置）；
            // 2) rotation 完全复制（蓝方水平朝向翻转由 CharacterCubeMarker.flipFacing 经 Spine 骨骼级 FlipX/ScaleX=-1 处理，不要再取反任何旋转轴，否则会倒转）。
            bluePose.positionOffset = new Vector3(-redPose.positionOffset.x, redPose.positionOffset.y, redPose.positionOffset.z);
            bluePose.rotationOffset = redPose.rotationOffset;
            bluePose.scaleOffset = redPose.scaleOffset;
            EditorUtility.SetDirty(bluePose);

            // 同步 fallback 字段，确保运行时 SlotPose 读不到时仍有兜底。
            SyncSlotPoseToMarkerFallback(redGo);
            SyncSlotPoseToMarkerFallback(blueGo);
            synced++;
        }

        if (synced > 0)
        {
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        }
        statusMsg = $"已从红方同步 {synced} 个 Aibo 位姿到蓝方（position.x 取反，rotation 原样复制，朝向交 flipFacing）。";
    }

}
#endif
