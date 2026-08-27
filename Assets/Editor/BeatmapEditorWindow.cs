using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MusicalSprite.Editor
{
    /// <summary>
    /// 谱面编辑器 + 谱面仓库。
    /// 菜单：Tools > Musical Sprite > Beatmap Editor
    ///
    /// 设计说明：
    /// - 编辑器内部维护一份「单边 4 音轨谱面」（ChartNote: time + lane）。
    /// - 每个音符的 time = 音符圆心抵达判定线的时刻（秒），与何时发射无关；
    ///   不同难度只整体改变音符移动速度，不影响 time 这一判定时刻，从而保证
    ///   音符与判定线（以及音乐）在任意速度下都精确对齐。
    /// - 保存时把同一份谱面复制成 side=0（左玩家）与 side=1（右玩家）两份，
    ///   以匹配游戏现有「左右谱面完全相同」的约定（NoteSpawner 按 side 过滤生成）。
    /// - 每个谱面存成一个 BeatmapSO 资产（含音符与音乐素材），放在 Assets/Beatmaps/ 下，可纳入 git 仓库。
    /// - 仓库列表支持：编辑（载入本编辑器）、调用（设为游戏启动谱面）、删除（从仓库移除）。
    /// </summary>
    public class BeatmapEditorWindow : EditorWindow
    {
        // ---------- 谱面数据（编辑器内部，单边 4 轨） ----------
        [System.Serializable]
        private class ChartNote
        {
            public float time;
            public int lane;
            public NoteData.NoteType type = NoteData.NoteType.Tap;
            public bool isSmallTap = false;   // SmallTap：半径更小、统一 PASS
            public int chainTapCount = 3;      // ChainTap：需要连续命中的次数
            public float holdDuration = 0f;   // 仅旧式 2 节点 Hold：持续时长（秒）
            public int holdEndLane = 0;       // 仅旧式 2 节点 Hold：结束音符所在轨道
            // Hold / Linked 长按的节点时刻与轨道。Linked 的 lane 表示双轨中的第一轨（0-2）。
            public float[] holdTimes;
            public int[] holdLanes;
            // 逐节点宽度（与 holdLanes 一一对应）：1=普通单轨节点，2=连轨节点（覆盖相邻两轨）。
            // 链接模式中不同节点可独立设宽度，不再整条链共用一个 type。
            public int[] holdLaneSpans;
        }

        /// <summary>
        /// 谱面文本备份（纵深防御层）。与 .asset 同源同步写出的 JSON 镜像，
        /// 用于"资产序列化万一再丢连轨宽度时"一次性恢复，也便于人直接阅读 / 手改谱面。
        /// 仅含 bpm 与音符数组（含 holdLaneSpans），不含任何运行期引用类型。
        /// </summary>
        [System.Serializable]
        private class BeatmapTextDump
        {
            public float bpm;
            public NoteData[] notes;
            public float[] markers;
        }

        [SerializeField] private string beatmapName = "NewBeatmap";
        [SerializeField] private float bpm = 128f;
        [SerializeField] private float songLength = 60f;
        [SerializeField] private List<ChartNote> notes = new List<ChartNote>();
        private string currentEditingPath = ""; // 正在编辑的资产路径；为空表示新谱面

        // ---------- 撤销 ----------
        // 撤销栈：每一项是某一操作“之前”的 notes 完整快照（深拷贝）。
        // 入栈发生在任何会改变音符列表的操作之前，出栈（撤销）即恢复到上一次操作前的状态。
        private List<List<ChartNote>> undoStack = new List<List<ChartNote>>();
        private const int UndoCap = 50; // 最多保存 50 步，超出后丢弃最早的

        // ---------- 视图参数 ----------
        [SerializeField] private float pixelsPerSecond = 60f;
        [SerializeField] private float viewStartTime = 0f;
        private const float LaneLabelWidth = 64f;
        private const float LaneHeight = 36f;
        private const float RulerHeight = 24f;
        private const float MarkerZoneHeight = 22f;   // 标记条：时间轴正上方独立 22px 条带，三角旗标在此，左键选中/右键删除，不参与播放头拖动
        [SerializeField] private bool snapToBeat = true;

        // ---------- 标记（快捷键 F，吸附 ±0.25s） ----------
        [SerializeField] private List<float> markers = new List<float>();

        // ---------- 播放倍速（1 / 0.5 / 0.25） ----------
        [SerializeField] private float playbackSpeed = 1f;

        // ---------- 拖拽播放头（红线） ----------
        private bool dragPlayhead = false;

        // ---------- 选择 / 拖拽 ----------
        private int selectedIndex = -1;
        private int dragIndex = -1;
        private ChartNote dragNote;
        private Vector2 dragStartMouse;
        private float dragStartNoteTime;
        private int dragStartNoteLane;

        // ---------- 链接模式（多次点击成链，可任意多节点 Hold） ----------
        [SerializeField] private bool linkingMode = false;   // G：点按成链（多节点逐节点连）
        [SerializeField] private bool pointLinkMode = false;  // H：点链模式（按下 A 拖到 B 生成 2 节点 Hold，纯滑动）
        private bool linkingActive = false;                 // 是否正在编辑一条链
        private List<float> linkTimes = new List<float>();  // 已落下的节点时刻
        private List<int> linkLanes = new List<int>();      // 已落下的节点轨道
        private List<int> linkLaneSpans = new List<int>();  // 已落下的节点宽度（1=普通单轨，2=连轨），逐节点独立
        private int linkLastIndex = -1;                     // 正在编辑的 ChartNote 在 notes 中的索引（多节点 Hold 本体）
        private bool linkingLinked = false;                 // 当前正在编辑的是双轨连轨链

        // ---------- 链接模式：左键按住不放 + 上下拖动 => 连轨音符 ----------
        // 左键按下后不直接落节点，而是记录起始信息；到 MouseUp 才决定：
        //  - 仅点击（无上下拖动）=> 落单轨节点（保持原有链接逻辑）
        //  - 按住并上下拖动 >=1 轨 => 制作连轨音符（覆盖相邻两轨）
        private bool pendingLinkDown = false;               // 左键已按下、等待 MouseUp 提交
        private Vector2 linkDownMouse;                      // 按下时鼠标位置
        private int linkDownLane;                           // 按下时所在轨道
        private float linkDownTime;                         // 按下时的 time（未吸附）

        // ---------- 点链模式 (H)：连接两个已有音符 ----------
        private int pointLinkSource = -1;                   // H：源音符索引（拖动起点，必须点在已有音符上）
        private bool pointLinkDragging = false;             // H：正在从源音符拖向目标音符
        private bool snapMarkers = true;                    // 标记吸附开关（F 打点是否吸附 ±0.25s）
        private int selectedMarker = -1;                    // 当前选中的标记索引（左键点三角选中）
        private int draggingMarkerIndex = -1;               // 标记拖拽中：正在被左键拖动重定位的标记索引（-1 表示未拖拽）

        // ---------- 点击音符与连轨音符创建 ----------
        [SerializeField] private bool placeSmallTapMode = false;
        [SerializeField] private bool placeChainTapMode = false;
        [SerializeField] private int chainTapCount = 3;

        // ---------- 播放预览 ----------
        private bool isPlaying;
        private double playStartEditorTime;
        private float playStartOffset;
        [SerializeField] private float playTime;
        // 谱面绑定的音乐素材（保存到 BeatmapSO.audioClip），编辑器预览与游戏运行时同步播放
        private AudioClip beatmapAudioClip;
        private AudioSource previewSource;
        // 波形图：每个 clip 的混音单声道样本缓存（避免每帧重读 PCM）；高度固定 60px
        private Dictionary<AudioClip, float[]> _waveCache = new Dictionary<AudioClip, float[]>();
        private static readonly float WaveformHeight = 240f; // 波形图放大 4 倍（原 60f），便于校谱

        // ---------- 仓库 ----------
        private Vector2 libScroll;
        private const string BeatmapsDir = "Assets/Beatmaps";
        private const string ActiveBeatmapKey = "MusicalSprite/ActiveBeatmap";

        // ---------- 颜色 ----------
        private static readonly Color[] LaneColors = new Color[]
        {
            new Color(1f, 0.45f, 0.45f),
            new Color(0.45f, 1f, 0.55f),
            new Color(0.5f, 0.7f, 1f),
            new Color(1f, 0.9f, 0.35f)
        };
        private static readonly Color EvenLane = new Color(0.20f, 0.20f, 0.23f);
        private static readonly Color OddLane = new Color(0.13f, 0.13f, 0.16f);

        [MenuItem("Tools/Musical Sprite/Beatmap Editor")]
        public static void ShowWindow()
        {
            GetWindow<BeatmapEditorWindow>("谱面编辑器");
        }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        StopPreview();
            if (previewSource != null)
            {
                DestroyImmediate(previewSource.gameObject);
                previewSource = null;
            }
        }

        private void OnEditorUpdate()
        {
            if (!isPlaying) return;
            // 倍速：以倍速系数推进播放时间（切换倍速时已在 SetPlaybackSpeed 重锚起点，不会跳变）
            float elapsed = (float)(EditorApplication.timeSinceStartup - playStartEditorTime) * playbackSpeed;
            playTime = elapsed + playStartOffset;
            if (playTime > songLength)
            {
                StopPlayback();
            }
        Repaint();
    }

    // ===================================================================
    // 谱面自愈：进入 PlayMode 前以 .json 文本备份重建被截断的 .asset
    // ===================================================================
    private void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        // 进入 PlayMode 前（ExitingEditMode）：把所有「资产音符数明显少于文本备份」的谱面，
        // 以同目录 .json 为准重建 .asset 并落盘。无论根因如何，运行时一定拿到健康谱面（用户 2026-08-27 要求）。
        if (change != PlayModeStateChange.ExitingEditMode) return;
        if (!AssetDatabase.IsValidFolder(BeatmapsDir)) return;
        string[] guids = AssetDatabase.FindAssets("t:BeatmapSO", new[] { BeatmapsDir });
        foreach (var g in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(g);
            if (!path.ToLowerInvariant().EndsWith(".asset")) continue;
            var bm = AssetDatabase.LoadAssetAtPath<BeatmapSO>(path);
            if (bm == null) continue;
            int assetSide0 = bm.notes != null ? bm.notes.Count(n => n.side == 0) : 0;
            int jsonSide0 = CountSide0InJson(GetJsonPath(path));
            if (jsonSide0 > assetSide0 + 1)
            {
                RebuildAssetFromJson(path);
                Debug.Log($"[谱面编辑器] ExitingEditMode 自愈：{Path.GetFileNameWithoutExtension(path)}.asset 已从 .json 重建 ({assetSide0}→{jsonSide0} 音符)");
            }
        }
    }

    /// <summary>以同目录 .json 文本备份为准重建 .asset 的 notes 并落盘（不触动编辑器内 notes，避免覆盖用户正在编辑的谱面）。</summary>
    private static void RebuildAssetFromJson(string assetPath)
    {
        string jsonPath = GetJsonPath(assetPath);
        if (!File.Exists(jsonPath)) return;
        try
        {
            string json = File.ReadAllText(jsonPath);
            BeatmapTextDump dump = JsonUtility.FromJson<BeatmapTextDump>(json);
            if (dump == null || dump.notes == null) return;
            var bm = AssetDatabase.LoadAssetAtPath<BeatmapSO>(assetPath);
            if (bm == null) return;
            bm.notes = dump.notes;       // .json 含完整 side0/side1，直接以文本备份为准
            bm.bpm = dump.bpm;
            EditorUtility.SetDirty(bm);
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[谱面编辑器] 自愈重建失败（{assetPath}）：{e.Message}");
        }
    }

    // ===================================================================
    // GUI
    // ===================================================================
        private void OnGUI()
        {
            // Ctrl/Cmd + Z 撤销。
            // Unity 6 没有任何"文本框正在编辑"的官方 API 能直接调用（EditorGUIUtility /
            // GUIUtility 都没有 isEditingTextField/textFieldHasFocus 这类成员），
            // 而 IMGUI 的 EditorGUILayout.TextField 又根本不响应 Ctrl+Z，
            // 因此无需再绕开——Ctrl+Z 直接走我们的撤销栈，绝不冲突。
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Z &&
                (Event.current.control || Event.current.command))
            {
                Undo();
                Event.current.Use();
                return;
            }

            DrawHeader();
            EditorGUILayout.Space(6);
            DrawTimeline();
            EditorGUILayout.Space(6);
            DrawLibrary();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("谱面编辑器（4 音轨 Timeline）", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            beatmapName = EditorGUILayout.TextField("谱面名称", beatmapName, GUILayout.Width(280));
            float bpmIn = EditorGUILayout.DelayedFloatField("BPM", bpm, GUILayout.Width(120));
            if (!Mathf.Approximately(bpmIn, bpm))
            {
                float oldBpm = bpm;
                bpm = Mathf.Max(1f, bpmIn);
                // 改 BPM：把所有音符 + 标记按 旧BPM/新BPM 同比例缩放，使二者一起重对齐到新节拍网格（可逆）
                RescaleChart(oldBpm / bpm);
                AutoExtendSongLength();
            }
            float slIn = EditorGUILayout.DelayedFloatField("歌曲长度(秒)", songLength, GUILayout.Width(140));
            if (!Mathf.Approximately(slIn, songLength))
            {
                songLength = Mathf.Max(0f, slIn);
                AutoExtendSongLength();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            snapToBeat = EditorGUILayout.ToggleLeft("吸附到拍", snapToBeat, GUILayout.Width(90));
            snapMarkers = EditorGUILayout.ToggleLeft("标记吸附", snapMarkers, GUILayout.Width(90));
            // 链点模式(G) 与 点链模式(H) 互斥
            bool newLink = EditorGUILayout.ToggleLeft("链点模式(G)", linkingMode, GUILayout.Width(110));
            bool newPoint = EditorGUILayout.ToggleLeft("点链模式(H)", pointLinkMode, GUILayout.Width(110));
            if (newLink != linkingMode) { linkingMode = newLink; if (linkingMode) { pointLinkMode = false; ResetLinkState(); } }
            if (newPoint != pointLinkMode) { pointLinkMode = newPoint; if (pointLinkMode) { linkingMode = false; ResetLinkState(); } }
            placeSmallTapMode = EditorGUILayout.ToggleLeft("小圈点击", placeSmallTapMode, GUILayout.Width(100));
            placeChainTapMode = EditorGUILayout.ToggleLeft("连点音符", placeChainTapMode, GUILayout.Width(90));
            if (placeChainTapMode)
            {
                int inputCount = EditorGUILayout.DelayedIntField("次数", Mathf.Clamp(chainTapCount, 3, 10), GUILayout.Width(110));
                chainTapCount = Mathf.Clamp(inputCount, 3, 10);
            }
            pixelsPerSecond = EditorGUILayout.Slider("缩放(像素/秒)", pixelsPerSecond, 10f, 480f, GUILayout.Width(240));
            beatmapAudioClip = (AudioClip)EditorGUILayout.ObjectField("音乐素材", beatmapAudioClip, typeof(AudioClip), false, GUILayout.Width(220));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("播放倍速", GUILayout.Width(70));
            if (GUILayout.Button("1x", GUILayout.Width(50))) SetPlaybackSpeed(1f);
            if (GUILayout.Button("0.5x", GUILayout.Width(50))) SetPlaybackSpeed(0.5f);
            if (GUILayout.Button("0.25x", GUILayout.Width(60))) SetPlaybackSpeed(0.25f);
            EditorGUILayout.LabelField($"当前：{playbackSpeed:F2}x", GUILayout.Width(90));
            EditorGUILayout.EndHorizontal();

            if (selectedIndex >= 0 && selectedIndex < notes.Count && notes[selectedIndex].type == NoteData.NoteType.ChainTap)
            {
                EditorGUILayout.BeginHorizontal();
                int currentCount = Mathf.Clamp(notes[selectedIndex].chainTapCount, 3, 10);
                int selectedCount = EditorGUILayout.DelayedIntField("选中连点次数", currentCount, GUILayout.Width(220));
                selectedCount = Mathf.Clamp(selectedCount, 3, 10);
                if (selectedCount != notes[selectedIndex].chainTapCount)
                {
                    PushUndo();
                    notes[selectedIndex].chainTapCount = selectedCount;
                    chainTapCount = selectedCount;
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建", GUILayout.Width(60))) NewChart();
            if (GUILayout.Button("保存", GUILayout.Width(60))) SaveBeatmap();
            if (GUILayout.Button("从文本恢复", GUILayout.Width(80)))
            {
                if (!string.IsNullOrEmpty(currentEditingPath))
                {
                    RestoreFromText(currentEditingPath);
                }
                else
                {
                    string p = EditorUtility.OpenFilePanel("选择谱面文本备份(.json)", BeatmapsDir, "json");
                    if (!string.IsNullOrEmpty(p))
                        RestoreFromText(Path.ChangeExtension(p, ".asset"));
                }
            }
            if (!isPlaying)
            {
                if (GUILayout.Button("播放", GUILayout.Width(60))) Play();
            }
            else
            {
                if (GUILayout.Button("暂停", GUILayout.Width(60))) Pause();
            }
            if (GUILayout.Button("停止", GUILayout.Width(60))) StopPlayback();
            if (GUILayout.Button("撤销", GUILayout.Width(60))) Undo();
            EditorGUILayout.LabelField($"音符数：{notes.Count}    标记：{markers.Count}    当前时间：{playTime:F2}s", GUILayout.Width(320));
            EditorGUILayout.EndHorizontal();

            string activePath = EditorPrefs.GetString(ActiveBeatmapKey, "");
            string activeName = string.IsNullOrEmpty(activePath) ? "（未调用，使用 Demo 谱面）"
                : "★ 调用中：" + Path.GetFileNameWithoutExtension(activePath);
            EditorGUILayout.HelpBox(
                "音符的 time = 音符圆心抵达判定线的时刻（非发射时刻）；改难度只改移动速度，不影响该时刻。\n" +
                "普通音符：轨道区单击=加音符；拖拽=移动；右键/Delete=删除；点击刻度尺=定位播放头。按 D=在当前红线位置（轨道2）加一条普通点击音符。\n" +
                "时间轴操作：滚轮=缩放（以光标为锚点）；在标记条下方的红线（含刻度尺与音轨区）上按住拖动=拖动播放头（校听）；空格=播放/暂停；倍速按钮=1x/0.5x/0.25x（音频同步变速）。\n" +
                "标记（校谱分段用，不影响游玩）：F 键在播放头处打标记（自动吸附 ±0.25s 内最近音符，否则吸附到 0.25s 网格）。标记为亮白色线 + 时间轴正上方「标记条」内的三角旗标（独立条带，整条高度可点）；左键点三角=选中并可拖拽重定位，右键点三角=删除，该条不参与播放头拖动。\n" +
                "时间轴长度跟随：BPM 或歌曲长度提交后自动扩时轴（适配最后音符 + 4s + 音乐长度），无需手动按钮；改 BPM 时音符与标记会按新旧 BPM 比例一起缩放重对齐。\n" +
                "链点模式：快捷键 G 开关；勾选后单击落节点、释放后再点下一个节点，依次累加成多节点链。右键点「链接线」= 在该段断开（前后各自保留为独立音符）；右键空白处=收尾整条链。\n" +
                "点链模式：快捷键 H 开关（与 G 互斥）。在第一个已有音符上左键按下、拖到第二个已有音符松开 = 把这两个音符链接成一条按住音符（纯链接，各自节点属性/连轨宽度原样保留，按时间排序串成链）。没点中音符或松开在空白处 = 取消，不生成任何东西。\n" +
                "连轨音符（链点模式专属）：在 G 模式下，左键按下并上下拖动 >=1 轨再松开 => 该节点为连轨（覆盖相邻两轨，width=2）。\n" +
                "连点音符：勾选「连点音符」后单击生成普通大点击外形的连续点击音符；次数框手动输入 3-10 次，选中后可在上方修改。\n" +
                "挂上「音乐素材」后点播放可听音校谱（波形图已放大 4 倍）；保存并「调用」后，下一次「搭建完整场景」将同步播放本谱面与音乐。\n" + activeName,
                MessageType.Info);
        }

        private void DrawTimeline()
        {
            Rect baseRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(RulerHeight + 4 * LaneHeight + MarkerZoneHeight));

            // 背景
            EditorGUI.DrawRect(baseRect, new Color(0.08f, 0.08f, 0.10f));

            float timeX0 = baseRect.x + LaneLabelWidth;
            float visibleWidth = baseRect.width - LaneLabelWidth;
            float visibleSeconds = visibleWidth / pixelsPerSecond;
            if (visibleSeconds < 0.01f) visibleSeconds = 1f;

            // 滚动钳制
            float maxStart = Mathf.Max(0f, songLength - visibleSeconds);
            viewStartTime = Mathf.Clamp(viewStartTime, 0f, maxStart);

            // ---- 标记条（独立条带，位于时间轴正上方）----
            EditorGUI.DrawRect(new Rect(baseRect.x, baseRect.y, baseRect.width, MarkerZoneHeight), new Color(0.10f, 0.10f, 0.14f));

            // ---- 刻度尺 ----
            EditorGUI.DrawRect(new Rect(baseRect.x, baseRect.y + MarkerZoneHeight, baseRect.width, RulerHeight), new Color(0.16f, 0.16f, 0.2f));
            float secPerBeat = 60f / Mathf.Max(0.001f, bpm);
            int firstBeat = Mathf.FloorToInt(viewStartTime / secPerBeat);
            int lastBeat = Mathf.CeilToInt((viewStartTime + visibleSeconds) / secPerBeat);
            for (int b = firstBeat; b <= lastBeat; b++)
            {
                float t = b * secPerBeat;
                float x = timeX0 + (t - viewStartTime) * pixelsPerSecond;
                if (x < timeX0 - 1 || x > baseRect.x + baseRect.width + 1) continue;
                bool major = (b % 4 == 0);
                Color tickCol = major ? new Color(1f, 1f, 1f, 0.85f) : new Color(1f, 1f, 1f, 0.3f);
                EditorGUI.DrawRect(new Rect(x, baseRect.y + MarkerZoneHeight + 4, 1, RulerHeight - 6), tickCol);
                if (major)
                {
                    GUI.Label(new Rect(x + 3, baseRect.y + MarkerZoneHeight + 3, 70, 16), $"{b}拍", EditorStyles.miniLabel);
                }
            }

            // ---- 标记（F 键打点，右键删除；吸附 ±0.25s）----
            // 线改为亮白色（高对比）；三角朝下画在顶部「标记条」内（独立于 ruler/lane，才能稳定被左键选中）
            for (int mi = 0; mi < markers.Count; mi++)
            {
                float mx = timeX0 + (markers[mi] - viewStartTime) * pixelsPerSecond;
                if (mx < timeX0 - 8 || mx > baseRect.x + baseRect.width + 8) continue;
                // 贯穿时间轴的亮白色细线（2px，更醒目）
                EditorGUI.DrawRect(new Rect(mx - 0.5f, baseRect.y + MarkerZoneHeight, 2f, baseRect.height - MarkerZoneHeight), new Color(1f, 1f, 1f, 0.85f));
                // 三角旗标画在 ruler 顶部 16px 标记命中带内（apex 朝下指向白线），脱离轨道点击区才能被选中
                // EditorWindow 中画 Handles 必须用 BeginGUI/EndGUI 包住，否则不渲染（这就是之前看不到三角的原因）
                // 颜色用高对比琥珀色，保证在亮/暗背景上都清晰可见
                if (Event.current.type == EventType.Repaint)
                {
                    Vector3[] tri = new Vector3[]
                    {
                        new Vector3(mx - 8f, baseRect.y + 4f, 0f),                                 // 左上
                        new Vector3(mx + 8f, baseRect.y + 4f, 0f),                                 // 右上
                        new Vector3(mx, baseRect.y + 15f, 0f)                                      // 底部尖（朝下指向白线）
                    };
                    Handles.BeginGUI();
                    Handles.color = (mi == selectedMarker) ? new Color(1f, 1f, 1f, 1f) : new Color(1f, 0.6f, 0.05f, 1f);
                    Handles.DrawAAConvexPolygon(tri);
                    if (mi == selectedMarker)
                    {
                        // 选中态：在三角外再画一圈高亮
                        Handles.color = new Color(1f, 1f, 1f, 1f);
                        Handles.DrawWireDisc(new Vector2(mx, baseRect.y + 10f), Vector3.forward, 11f);
                    }
                    Handles.EndGUI();
                }
            }

            // ---- 轨道 ----
            for (int lane = 0; lane < 4; lane++)
            {
                float y = baseRect.y + MarkerZoneHeight + RulerHeight + (3 - lane) * LaneHeight;
                EditorGUI.DrawRect(new Rect(baseRect.x, y, baseRect.width, LaneHeight),
                    lane % 2 == 0 ? EvenLane : OddLane);
                // 轨道分隔
                EditorGUI.DrawRect(new Rect(baseRect.x, y, baseRect.width, 1), new Color(1f, 1f, 1f, 0.08f));
                GUI.Label(new Rect(baseRect.x + 6, y + LaneHeight / 2 - 9, LaneLabelWidth - 10, 18),
                    $"轨道 {lane}", EditorStyles.miniLabel);
            }

            // ---- 音符 ----
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                bool isLinked = n.type == NoteData.NoteType.Linked;
                bool isHold = n.type == NoteData.NoteType.Hold ||
                    (isLinked && n.holdTimes != null && n.holdLanes != null && n.holdTimes.Length >= 2 && n.holdLanes.Length >= 2);

                if (isHold)
                {
                    // 多节点 Hold：遍历所有节点画折线 + 每个节点画方块。
                    // 逐节点宽度（holdLaneSpans）：普通节点 1 轨、连轨节点 2 轨，互不影响。
                    float[] ts;
                    int[] ls;
                    if (n.holdTimes != null && n.holdLanes != null && n.holdTimes.Length >= 2 && n.holdLanes.Length >= 2)
                    {
                        ts = n.holdTimes;
                        ls = n.holdLanes;
                    }
                    else
                    {
                        // 退化 2 节点：head=tail 直线
                        ts = new float[] { n.time, n.time + Mathf.Max(0.1f, n.holdDuration) };
                        ls = new int[] { n.lane, n.holdEndLane };
                    }

                    Color lineCol = (i == selectedIndex) ? Color.yellow : new Color(0.3f, 0.9f, 1f);
                    float thinTh = 4f;
                    float thickTh = LaneHeight * 1.45f;
                    for (int k = 0; k < ts.Length - 1; k++)
                    {
                        float xA = timeX0 + (ts[k] - viewStartTime) * pixelsPerSecond;
                        bool aLinked = NodeIsLinked(n, k);
                        bool bLinked = NodeIsLinked(n, k + 1);
                        float yA = GetNoteY(baseRect, ls[k], aLinked);
                        float xB = timeX0 + (ts[k + 1] - viewStartTime) * pixelsPerSecond;
                        float yB = GetNoteY(baseRect, ls[k + 1], bLinked);
                        if (xB < timeX0 - 16 || xA > baseRect.x + baseRect.width + 16) continue;
                        Vector2 pa = new Vector2(xA, yA);
                        Vector2 pb = new Vector2(xB, yB);
                        // 两端同为连轨时使用运行时一致的阶梯结构；普通 Hold 仍画细直线。
                        if (aLinked && bLinked)
                        {
                            DrawLinkedStair(pa, pb, thickTh, lineCol);
                        }
                        else if (!aLinked && !bLinked)
                        {
                            DrawThickLine(pa, pb, thinTh, lineCol);
                        }
                        else
                        {
                            int steps = 10;
                            float aTh = aLinked ? thickTh : thinTh;
                            float bTh = bLinked ? thickTh : thinTh;
                            for (int s2 = 0; s2 < steps; s2++)
                            {
                                float t0 = (float)s2 / steps, t1 = (float)(s2 + 1) / steps;
                                Vector2 p0 = Vector2.Lerp(pa, pb, t0);
                                Vector2 p1 = Vector2.Lerp(pa, pb, t1);
                                float th = Mathf.Lerp(aTh, bTh, (t0 + t1) * 0.5f);
                                DrawThickLine(p0, p1, th, lineCol);
                            }
                        }
                    }
                    // 节点方块（逐节点宽度）
                    for (int k = 0; k < ts.Length; k++)
                    {
                        float x = timeX0 + (ts[k] - viewStartTime) * pixelsPerSecond;
                        if (x < timeX0 - 16 || x > baseRect.x + baseRect.width + 16) continue;
                        bool nodeLinked = NodeIsLinked(n, k);
                        float y = GetNoteY(baseRect, ls[k], nodeLinked);
                        float nodeHeight = nodeLinked ? LaneHeight * 1.7f : 12f;
                        Rect nodeRect = new Rect(x - 6, y - nodeHeight * 0.5f, 12, nodeHeight);
                        Color nodeColor = (i == selectedIndex) ? Color.yellow : LaneColors[Mathf.Clamp(ls[k], 0, 3)];
                        if (nodeLinked)
                            DrawRoundedRect(nodeRect, 3f, nodeColor);
                        else
                            EditorGUI.DrawRect(nodeRect, nodeColor);
                    }
                }
                else
                {
                    float x = timeX0 + (n.time - viewStartTime) * pixelsPerSecond;
                    if (x < timeX0 - 16 || x > baseRect.x + baseRect.width + 16) continue;
                    float y = GetNoteY(baseRect, n.lane, isLinked);
                    Color c;
                    if (n.isSmallTap)
                        c = (i == selectedIndex) ? Color.yellow : new Color(0.7f, 0.4f, 1f); // 小型点击=紫色
                    else
                        c = (i == selectedIndex) ? Color.yellow : LaneColors[Mathf.Clamp(n.lane, 0, 3)];
                    if (i == selectedIndex)
                    {
                        EditorGUI.DrawRect(new Rect(x - 9, y - 9, 18, 18), new Color(1f, 1f, 0.2f, 0.35f));
                    }
                    // Linked 单节点纵向覆盖相邻两轨；普通 Tap / SmallTap 保持大小圈区别。
                    float half = n.isSmallTap ? 4f : 6f;
                    float height = isLinked ? LaneHeight * 1.7f : half * 2f;
                    Rect noteRect = new Rect(x - half, y - height * 0.5f, half * 2f, height);
                    if (isLinked)
                        DrawRoundedRect(noteRect, 3f, c);
                    else
                        EditorGUI.DrawRect(noteRect, c);
                    if (n.type == NoteData.NoteType.ChainTap)
                        DrawChainTapCount(new Rect(x - 10f, y - 9f, 20f, 18f), n.chainTapCount);
                }
            }

            // 链接模式跟随预览：最后已落节点 → 鼠标指针（明确表现"正在链接鼠标"）
            if (linkingMode && linkingActive && linkTimes.Count > 0 && !pendingLinkDown)
            {
                // 跟随线宽度取“最后已落节点”自身的宽度（普通=细线，连轨=粗宽带），而非整条链标志
                int lastSpan = (linkLaneSpans.Count > 0 && linkLaneSpans[linkLaneSpans.Count - 1] > 1) ? 2 : 1;
                bool lastLinked = lastSpan > 1;
                Vector2 cur = Event.current.mousePosition;
                float xLast = timeX0 + (linkTimes[linkTimes.Count - 1] - viewStartTime) * pixelsPerSecond;
                float yLast = GetNoteY(baseRect, linkLanes[linkLanes.Count - 1], lastLinked);
                // 亮蓝主线（连轨使用阶梯结构，普通 Hold 使用直线）
                Color previewColor = new Color(0.25f, 0.85f, 1f, 0.95f);
                if (lastLinked)
                    DrawLinkedStair(new Vector2(xLast, yLast), cur, LaneHeight * 1.45f, previewColor);
                else
                    DrawThickLine(new Vector2(xLast, yLast), cur, 5f, previewColor);
                // 鼠标处的"实时端点"圆环 + 上一节点的白色连接点，强调正在跟手
                if (Event.current.type == EventType.Repaint)
                {
                    float r = lastLinked ? LaneHeight * 0.55f : 7f;
                    Handles.color = new Color(0.25f, 0.95f, 1f, 1f);
                    Handles.DrawWireDisc(cur, Vector3.forward, r);
                    Handles.color = new Color(1f, 1f, 1f, 0.9f);
                    Handles.DrawSolidDisc(new Vector2(xLast, yLast), Vector3.forward, 4f);
                }
                GUI.Label(new Rect(cur.x + 10, cur.y - 22, 140, 18), "链接中…右键完成", EditorStyles.miniLabel);
            }

            // 点链模式 (H)：橡皮筋预览（源音符 -> 光标），明确表现"正在把两个音符链接起来"
            if (pointLinkMode && pointLinkDragging && pointLinkSource >= 0 && pointLinkSource < notes.Count)
            {
                var src = notes[pointLinkSource];
                float sx = timeX0 + (src.time - viewStartTime) * pixelsPerSecond;
                float sy = GetNoteY(baseRect, src.lane, src.type == NoteData.NoteType.Linked);
                Vector2 cur = Event.current.mousePosition;
                Color previewColor = new Color(1f, 0.6f, 0.1f, 0.95f);
                DrawThickLine(new Vector2(sx, sy), cur, 5f, previewColor);
                if (Event.current.type == EventType.Repaint)
                {
                    Handles.BeginGUI();
                    Handles.color = new Color(1f, 0.8f, 0.2f, 1f);
                    Handles.DrawWireDisc(cur, Vector3.forward, 8f);
                    Handles.DrawSolidDisc(new Vector2(sx, sy), Vector3.forward, 4f);
                    Handles.EndGUI();
                }
                GUI.Label(new Rect(cur.x + 10, cur.y - 22, 170, 18), "链接音符…拖到目标音符松开", EditorStyles.miniLabel);
            }

            // 链接模式：左键按住 + 上下拖动 => 连轨音符（覆盖相邻两轨）实时预览
            if (linkingMode && pendingLinkDown)
            {
                Vector2 cur = Event.current.mousePosition;
                float localY = cur.y - baseRect.y;
                int displayRow = Mathf.Clamp(Mathf.FloorToInt((localY - MarkerZoneHeight - RulerHeight) / LaneHeight), 0, 3);
                int endLane = 3 - displayRow;
                // 拖动方向（屏幕上"下"=lane 减小），钳制到相邻 1 轨
                int step = Mathf.Clamp(endLane - linkDownLane, -1, 1);
                bool crossLane = step != 0;
                // 与提交逻辑保持一致：拖动跨轨 => 2 轨连轨节点；否则为 1 轨普通节点
                int commitLane = crossLane ? Mathf.Clamp(Mathf.Min(linkDownLane, linkDownLane + step), 0, 2) : linkDownLane;
                int commitSpan = crossLane ? 2 : 1;

                if (commitSpan > 1)
                {
                    // 覆盖两轨的高亮带（commitLane 为上方轨，commitLane+1 为下方轨）
                    float bandTop = baseRect.y + MarkerZoneHeight + RulerHeight + (2 - commitLane) * LaneHeight;
                    EditorGUI.DrawRect(new Rect(baseRect.x, bandTop, baseRect.width, LaneHeight * 2f),
                        new Color(1f, 0.6f, 0.2f, 0.28f));
                }
                else
                {
                    // 单轨节点：仅高亮对应的一条轨道，避免预览比实际结果更宽
                    float bandTop = baseRect.y + MarkerZoneHeight + RulerHeight + (3 - commitLane) * LaneHeight;
                    EditorGUI.DrawRect(new Rect(baseRect.x, bandTop, baseRect.width, LaneHeight),
                        new Color(1f, 0.6f, 0.2f, 0.18f));
                }
                // 竖拖手柄线（按下点 -> 当前指针）
                DrawThickLine(linkDownMouse, cur, 3f, new Color(1f, 0.6f, 0.2f, 0.9f));
                // 落点标记
                if (Event.current.type == EventType.Repaint)
                {
                    float yNode = GetNoteY(baseRect, commitLane, commitSpan > 1);
                    Handles.color = new Color(1f, 0.85f, 0.3f, 1f);
                    Handles.DrawWireDisc(new Vector2(timeX0 + (linkDownTime - viewStartTime) * pixelsPerSecond, yNode),
                        Vector3.forward, LaneHeight * 0.55f);
                }
                string tip = step == 0
                    ? "松开=单轨节点（横向拖动无效，time 取按下点）"
                    : $"连轨音符 轨{commitLane}-{commitLane + 1}（松开继续链接，右键=普通连轨点击）";
                GUI.Label(new Rect(cur.x + 10, cur.y - 22, 280, 18), tip, EditorStyles.miniLabel);
            }

            // ---- 播放头 ----
            float phx = timeX0 + (playTime - viewStartTime) * pixelsPerSecond;
            EditorGUI.DrawRect(new Rect(phx, baseRect.y + MarkerZoneHeight, 2, baseRect.height - MarkerZoneHeight), Color.red);

            // 滚动条
            viewStartTime = GUILayout.HorizontalScrollbar(viewStartTime, visibleSeconds, 0f, songLength + 1f);

            HandleTimelineEvents(baseRect, timeX0);

            // 音乐素材波形图：紧贴轨道时间轴正下方，与 time 轴对齐，方便校谱（无素材时不画、高度不变）
            if (beatmapAudioClip != null)
            {
                Rect wfRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.ExpandWidth(true), GUILayout.Height(WaveformHeight));
                DrawWaveform(wfRect);
            }
        }

        private void HandleTimelineEvents(Rect baseRect, float timeX0)
        {
            Event e = Event.current;
            // 链接跟随阶段：鼠标移动时持续重绘，让"链接鼠标"的蓝线（或连轨音符预览）实时跟手
            if (e.type == EventType.MouseMove)
            {
                if (linkingMode && (linkingActive || pendingLinkDown)) { Repaint(); e.Use(); }
                return;
            }
            // 滚轮缩放（以光标为锚点）：在轨道时间轴上滚动即缩放，不影响其它区域
            if (e.type == EventType.ScrollWheel && baseRect.Contains(e.mousePosition))
            {
                float timeAtCursor = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
                float zoom = e.delta.y > 0f ? (1f / 1.15f) : 1.15f; // 上滚放大、下滚缩小
                pixelsPerSecond = Mathf.Clamp(pixelsPerSecond * zoom, 10f, 480f);
                float visibleSeconds = (baseRect.width - LaneLabelWidth) / pixelsPerSecond;
                viewStartTime = timeAtCursor - (e.mousePosition.x - timeX0) / pixelsPerSecond;
                viewStartTime = Mathf.Clamp(viewStartTime, 0f, Mathf.Max(0f, songLength - visibleSeconds));
                Repaint();
                e.Use();
                return;
            }
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag &&
                e.type != EventType.MouseUp && e.type != EventType.KeyDown) return;
            if (e.type == EventType.KeyDown)
            {
                // 仅在真正编辑文本框（名称/BPM 等）时让位给文本输入；勾选框/按钮焦点不算，
                // 否则点过「链接模式」勾选框后焦点残留，空格会被 Unity 默认行为拿去切换该勾选框、F 也被吞
                if (EditorGUIUtility.editingTextField) return;
                // G：切换链点模式（点按成链，多节点逐节点连）；与点链模式互斥
                if (e.keyCode == KeyCode.G)
                {
                    linkingMode = !linkingMode;
                    pointLinkMode = false;
                    if (!linkingMode) ResetLinkState();
                    Repaint();
                    e.Use();
                    return;
                }
                // H：切换点链模式（滑动 A→B 生成 2 节点 Hold，纯滑动）
                if (e.keyCode == KeyCode.H)
                {
                    pointLinkMode = !pointLinkMode;
                    linkingMode = false;
                    if (!pointLinkMode) ResetLinkState();
                    Repaint();
                    e.Use();
                    return;
                }
                // D：在当前播放头（红线）位置添加一条普通点击音符（轨道2 / lane 2）
                if (e.keyCode == KeyCode.D)
                {
                    float snapped = snapToBeat ? Snap(playTime) : playTime;
                    snapped = Mathf.Clamp(snapped, 0f, songLength);
                    var dn = new ChartNote { time = snapped, lane = 2 };
                    PushUndo();
                    notes.Add(dn);
                    selectedIndex = notes.Count - 1;
                    Repaint();
                    e.Use();
                    return;
                }
                // 空格：播放 / 暂停切换
                if (e.keyCode == KeyCode.Space)
                {
                    if (isPlaying) Pause(); else Play();
                    e.Use();
                    return;
                }
                // F：在播放头处打标记（吸附 ±0.25s）
                if (e.keyCode == KeyCode.F)
                {
                    AddMarkerAtPlayTime();
                    e.Use();
                    return;
                }
                if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedIndex >= 0)
                {
                    PushUndo();
                    notes.RemoveAt(selectedIndex);
                    selectedIndex = -1;
                    Repaint();
                    e.Use();
                }
                return;
            }

            // 处理挂起的连轨拖动落点：即使松开在区域外也提交，避免状态卡死
            if (e.type == EventType.MouseUp && pendingLinkDown)
            {
                CommitPendingLinkNode(baseRect, timeX0);
                pendingLinkDown = false;
                e.Use();
                return;
            }

            if (!baseRect.Contains(e.mousePosition)) return;

            float localY = e.mousePosition.y - baseRect.y;

            // ---- 标记条（时间轴正上方独立条带 MarkerZoneHeight，优先于播放头拖动）----
            // 左键点中三角旗标 => 选中并开始拖拽（之后 MouseDrag 中实时重定位）；右键 => 删除。
            // 该带不参与播放头拖动 / 加音符，专给标记。拖拽进行中放行到下方 MouseDrag/MouseUp 处理。
            if (localY < MarkerZoneHeight)
            {
                if (e.type == EventType.MouseDown)
                {
                    int mHit = HitTestMarkerTriangle(e.mousePosition, baseRect, timeX0);
                    if (mHit >= 0)
                    {
                        if (e.button == 0)
                        {
                            selectedMarker = mHit;
                            draggingMarkerIndex = mHit;   // 按下即进入拖拽预备，移动即重定位
                            Repaint();
                            e.Use();
                            return;
                        }
                        if (e.button == 1)
                        {
                            markers.RemoveAt(mHit);
                            if (selectedMarker == mHit) selectedMarker = -1;
                            else if (selectedMarker > mHit) selectedMarker--;
                            Repaint();
                            e.Use();
                            return;
                        }
                    }
                    // 标记带内空白点击：不触发播放头/音符（该带专用于标记）
                    e.Use();
                    return;
                }
                // MouseDrag / MouseUp：正在拖拽标记时放行到下方专用分支；否则吞掉
                if (draggingMarkerIndex < 0) { e.Use(); return; }
            }

            // ---- 播放头命中检测（高优先级，标记条下方整段：ruler + 音轨均可拖动）----
            // 红线拖动范围从标记条下沿一直到时间轴底部；标记条自身不参与，避免与标记操作冲突。
            if (e.type == EventType.MouseDown && e.button == 0 &&
                localY >= MarkerZoneHeight)
            {
                float phx = timeX0 + (playTime - viewStartTime) * pixelsPerSecond;
                if (Mathf.Abs(e.mousePosition.x - phx) <= 8f)
                {
                    dragPlayhead = true;
                    float t = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
                    playTime = Mathf.Clamp(t, 0f, songLength);
                    if (isPlaying && previewSource != null) { try { previewSource.time = playTime; } catch { } }
                    Repaint();
                    e.Use();
                    return;
                }
            }

            // 点击刻度尺 => 定位播放头（标记带下方的 ruler 区域，避开顶部标记带）
            if (localY >= MarkerZoneHeight && localY < MarkerZoneHeight + RulerHeight)
            {
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    float t = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
                    playTime = Mathf.Clamp(t, 0f, songLength);
                    Repaint();
                    e.Use();
                }
                return;
            }

            // 编辑器屏幕从上到下显示 3、2、1、0，与游戏从下到上的轨道编号一致。
            int displayRow = Mathf.Clamp(Mathf.FloorToInt((localY - MarkerZoneHeight - RulerHeight) / LaneHeight), 0, 3);
            int lane = 3 - displayRow;
            float time = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
            time = Mathf.Clamp(time, 0f, songLength);

            if (e.type == EventType.MouseDown)
            {
                int hit = HitTestNote(e.mousePosition, baseRect, timeX0);

                // 注：播放头命中检测已提升到上方（高优先级），这里不再重复

            // 右键：优先命中"链接线"则断连；其次删除标记；再次收尾整条活动链；最后删除音符
            if (e.button == 1)
            {
                // 右键标记：删除靠近光标的标记
                int hitMarker = HitTestMarker(e.mousePosition, baseRect, timeX0);
                if (hitMarker >= 0)
                {
                    markers.RemoveAt(hitMarker);
                    if (selectedMarker == hitMarker) selectedMarker = -1;
                    else if (selectedMarker > hitMarker) selectedMarker--;
                    Repaint();
                    e.Use();
                    return;
                }
                // 右键落在"链接线"上 = 断连（在最近的一段断开，前后各自保留为独立音符）
                int segNote, segIdx; bool isActive;
                if (HitTestLinkSegment(e.mousePosition, baseRect, timeX0, out segNote, out segIdx, out isActive))
                {
                    if (isActive) BreakActiveChainAt(segIdx);
                    else SplitHoldNoteAt(segNote, segIdx);
                    Repaint();
                    e.Use();
                    return;
                }
                // 活动链接态下右键空白处：收尾整条链（兼容旧习惯）
                if (linkingMode && linkingActive)
                {
                    FinishLinkingChain();
                    Repaint();
                    e.Use();
                    return;
                }
                if (hit >= 0)
                {
                    PushUndo();
                    notes.RemoveAt(hit);
                    if (selectedIndex == hit) selectedIndex = -1;
                    Repaint();
                    e.Use();
                }
                return;
            }

                    // 链点模式 (G) 或 点链模式 (H) 任一激活时，进入左键-延迟-提交逻辑
                    if (linkingMode || pointLinkMode)
                    {
                        if (e.button == 0)
                        {
                            // 点链模式 (H)：必须在已有音符上按下，之后拖到另一个音符松开 => 合并成链接（纯链接，不改音符属性）
                            if (pointLinkMode)
                            {
                                int hitNote = HitTestNote(e.mousePosition, baseRect, timeX0);
                                if (hitNote >= 0)
                                {
                                    pointLinkSource = hitNote;
                                    pointLinkDragging = true;
                                    Repaint();
                                    e.Use();
                                    return;
                                }
                                // 没点中已有音符：不动作
                                e.Use();
                                return;
                            }
                            // 链点模式 (G)：窗口热重载或删除正在编辑的链后，旧索引可能失效，直接清空重建
                            if (linkingActive && (linkLastIndex < 0 || linkLastIndex >= notes.Count))
                            {
                                linkingActive = false;
                                linkingLinked = false;
                                linkLastIndex = -1;
                                linkTimes.Clear();
                                linkLanes.Clear();
                            }
                            pendingLinkDown = true;
                            linkDownMouse = e.mousePosition;
                            linkDownLane = lane;
                            linkDownTime = time;
                            Repaint();
                            e.Use();
                            return;
                        }
                        // 链接模式下的右键已在上方统一处理为"断开链接"；其余按钮忽略
                        return;
                    }

                if (hit >= 0) // 选中并准备拖拽
                {
                    PushUndo(); // 在拖动改变音符位置前记录快照，撤销即可回到拖动前
                    selectedIndex = hit;
                    dragIndex = hit;
                    dragNote = notes[hit];
                    dragStartMouse = e.mousePosition;
                    dragStartNoteTime = dragNote.time;
                    dragStartNoteLane = dragNote.lane;
                }
                else if (placeChainTapMode)
                {
                    float snapped = snapToBeat ? Snap(time) : time;
                    var nn = new ChartNote
                    {
                        time = snapped,
                        lane = lane,
                        type = NoteData.NoteType.ChainTap,
                        chainTapCount = Mathf.Clamp(chainTapCount, 3, 10)
                    };
                    PushUndo();
                    notes.Add(nn);
                    selectedIndex = notes.Count - 1;
                    dragIndex = selectedIndex;
                    dragNote = nn;
                    dragStartMouse = e.mousePosition;
                    dragStartNoteTime = nn.time;
                    dragStartNoteLane = nn.lane;
                    Repaint();
                    e.Use();
                }
                else if (placeSmallTapMode) // 恢复普通点击音符的大小圈区分
                {
                    float snapped = snapToBeat ? Snap(time) : time;
                    var nn = new ChartNote
                    {
                        time = snapped,
                        lane = lane,
                        type = NoteData.NoteType.SmallTap,
                        isSmallTap = true
                    };
                    PushUndo();
                    notes.Add(nn);
                    selectedIndex = notes.Count - 1;
                    dragIndex = selectedIndex;
                    dragNote = nn;
                    dragStartMouse = e.mousePosition;
                    dragStartNoteTime = nn.time;
                    dragStartNoteLane = nn.lane;
                    Repaint();
                    e.Use();
                }
                else // 新增普通音符
                {
                    float snapped = snapToBeat ? Snap(time) : time;
                    var nn = new ChartNote { time = snapped, lane = lane };
                    PushUndo();
                    notes.Add(nn);
                    selectedIndex = notes.Count - 1;
                    dragIndex = selectedIndex;
                    dragNote = nn;
                    dragStartMouse = e.mousePosition;
                    dragStartNoteTime = nn.time;
                    dragStartNoteLane = nn.lane;
                }
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseDrag)
            {
                // 标记拖拽中：实时重定位（与 F 打点相同的吸附规则）
                if (draggingMarkerIndex >= 0)
                {
                    float nt = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
                    if (snapMarkers)
                    {
                        float nearest = float.MaxValue;
                        foreach (var n in notes)
                        {
                            float d = Mathf.Abs(n.time - nt);
                            if (d < nearest) nearest = d;
                            if (d <= 0.25f) { nt = n.time; break; }
                        }
                        if (nearest > 0.25f) nt = Mathf.Round(nt / 0.25f) * 0.25f;
                    }
                    nt = Mathf.Clamp(nt, 0f, songLength);
                    markers[draggingMarkerIndex] = nt;
                    Repaint();
                    e.Use();
                    return;
                }
                if (dragPlayhead)
                {
                    float t = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
                    playTime = Mathf.Clamp(t, 0f, songLength);
                    if (isPlaying && previewSource != null) { try { previewSource.time = playTime; } catch { } }
                    Repaint();
                    e.Use();
                    return;
                }
                if (pointLinkMode && pointLinkDragging)
                {
                    Repaint(); // 橡皮筋预览由 DrawTimeline 绘制（源音符 -> 光标）
                    e.Use();
                    return;
                }
                if (pendingLinkDown)
                {
                    Repaint(); // 连轨音符预览由 DrawTimeline 绘制
                    e.Use();
                    return;
                }
                if (linkingMode && linkingActive)
                {
                    Repaint(); // 跟随预览线由 DrawTimeline 绘制（使用 Event.current.mousePosition）
                    e.Use();
                    return;
                }
                if (dragIndex >= 0 && dragNote != null)
                {
                    float nt = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
                    nt = Mathf.Clamp(nt, 0f, songLength);
                    int dragDisplayRow = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y - baseRect.y - MarkerZoneHeight - RulerHeight) / LaneHeight), 0, 3);
                    int nlane = 3 - dragDisplayRow;

                    bool linkedHold = dragNote.type == NoteData.NoteType.Linked &&
                        dragNote.holdLanes != null && dragNote.holdTimes != null && dragNote.holdLanes.Length >= 2;
                    if (dragNote.type == NoteData.NoteType.Hold || linkedHold)
                    {
                        // 多节点 Hold 整体移动：head/tail 轨道差值保持，所有节点时间整体平移
                        int maxLane = dragNote.type == NoteData.NoteType.Linked ? 2 : 3;
                        int targetLane = Mathf.Clamp(nlane, 0, maxLane);
                        int delta = targetLane - dragNote.lane;
                        float dt = (snapToBeat ? Snap(nt) : nt) - dragNote.time;
                        dragNote.lane = targetLane;
                        if (dragNote.holdLanes != null && dragNote.holdTimes != null && dragNote.holdLanes.Length >= 2)
                        {
                            for (int k = 0; k < dragNote.holdLanes.Length; k++)
                            {
                                dragNote.holdLanes[k] = Mathf.Clamp(dragNote.holdLanes[k] + delta, 0, maxLane);
                                dragNote.holdTimes[k] = Mathf.Max(0f, dragNote.holdTimes[k] + dt);
                            }
                            dragNote.holdEndLane = dragNote.holdLanes[dragNote.holdLanes.Length - 1];
                            dragNote.time = dragNote.holdTimes[0];
                            dragNote.holdDuration = Mathf.Max(0.1f, dragNote.holdTimes[dragNote.holdTimes.Length - 1] - dragNote.holdTimes[0]);
                        }
                        else
                        {
                            dragNote.holdEndLane = Mathf.Clamp(dragNote.holdEndLane + delta, 0, maxLane);
                            dragNote.time = snapToBeat ? Snap(nt) : nt;
                        }
                    }
                    else
                    {
                        dragNote.time = snapToBeat ? Snap(nt) : nt;
                        dragNote.lane = dragNote.type == NoteData.NoteType.Linked ? Mathf.Clamp(nlane, 0, 2) : nlane;
                    }
                    Repaint();
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                if (draggingMarkerIndex >= 0)
                {
                    draggingMarkerIndex = -1;
                    Repaint();
                    e.Use();
                    return;
                }
                if (dragPlayhead)
                {
                    dragPlayhead = false;
                    Repaint();
                    e.Use();
                    return;
                }
                if (pointLinkMode && pointLinkDragging)
                {
                    int hitB = HitTestNote(e.mousePosition, baseRect, timeX0);
                    if (hitB >= 0 && hitB != pointLinkSource)
                        MergeNotesIntoChain(pointLinkSource, hitB);
                    pointLinkSource = -1;
                    pointLinkDragging = false;
                    Repaint();
                    e.Use();
                    return;
                }
                if (dragIndex >= 0)
                {
                    dragIndex = -1;
                    dragNote = null;
                    Repaint();
                    e.Use();
                }
            }
        }

        private int HitTestNote(Vector2 mouse, Rect baseRect, float timeX0)
        {
            for (int i = notes.Count - 1; i >= 0; i--)
            {
                var n = notes[i];
                bool isLinked = n.type == NoteData.NoteType.Linked;
                bool isHold = n.type == NoteData.NoteType.Hold ||
                    (isLinked && n.holdTimes != null && n.holdLanes != null && n.holdTimes.Length >= 2 && n.holdLanes.Length >= 2);
                if (isHold)
                {
                    float[] ts;
                    int[] ls;
                    if (n.holdTimes != null && n.holdLanes != null && n.holdTimes.Length >= 2 && n.holdLanes.Length >= 2)
                    {
                        ts = n.holdTimes; ls = n.holdLanes;
                    }
                    else
                    {
                        ts = new float[] { n.time, n.time + Mathf.Max(0.1f, n.holdDuration) };
                        ls = new int[] { n.lane, n.holdEndLane };
                    }
                    for (int k = 0; k < ts.Length; k++)
                    {
                        float x = timeX0 + (ts[k] - viewStartTime) * pixelsPerSecond;
                        float y = GetNoteY(baseRect, ls[k], isLinked);
                        if (Near(mouse, x, y, isLinked ? LaneHeight : 18f)) return i;
                    }
                }
                else
                {
                    float x = timeX0 + (n.time - viewStartTime) * pixelsPerSecond;
                    float y = GetNoteY(baseRect, n.lane, isLinked);
                    if (Near(mouse, x, y, isLinked ? LaneHeight : 18f)) return i;
                }
            }
            return -1;
        }

        /// <summary>在播放头当前位置打一个标记：优先吸附到 ±0.25s 内最近的音符；否则吸附到最近的 0.25s 网格。</summary>
        private void AddMarkerAtPlayTime()
        {
            float t = playTime;
            if (snapMarkers)
            {
                float nearest = float.MaxValue;
                foreach (var n in notes)
                {
                    float d = Mathf.Abs(n.time - t);
                    if (d < nearest) nearest = d;
                    if (d <= 0.25f) { t = n.time; break; } // 吸附到最近音符（阈值 ±0.25s）
                }
                if (nearest > 0.25f)
                    t = Mathf.Round(t / 0.25f) * 0.25f;     // 无邻近音符则吸附到 0.25s 网格
            }
            // snapMarkers 关闭时：t 直接等于播放头当前时刻（不吸附）
            t = Mathf.Clamp(t, 0f, songLength);
            markers.Add(t);
            selectedMarker = markers.Count - 1;
            Repaint();
            ShowNotification(new GUIContent($"已添加标记 @ {t:F2}s"));
        }

        /// <summary>命中测试：返回光标附近（水平 ±7px）的标记索引，否则 -1。</summary>
        private int HitTestMarker(Vector2 mouse, Rect baseRect, float timeX0)
        {
            // 仅用于 ruler 下方轨道区：点中标记白线（水平 ±7px、纵向整段）即命中，右键删除用。
            // 标记条（时间轴正上方 MarkerZoneHeight）的选中/删除由 HitTestMarkerTriangle + MouseDown 标记条分支处理。
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                float x = timeX0 + (markers[i] - viewStartTime) * pixelsPerSecond;
                if (Mathf.Abs(mouse.x - x) <= 7f &&
                    mouse.y >= baseRect.y + MarkerZoneHeight && mouse.y <= baseRect.y + baseRect.height)
                    return i;
            }
            return -1;
        }

        /// <summary>命中测试：光标是否落在某条"链接线"（相邻两节点之间的连线）上。
        /// 命中返回 true，并通过 out 给出：segNote=音符索引（活动链为 -1）、segIdx=段号（节点 segIdx 与 segIdx+1 之间）、isActive=是否当前正在编辑的活动链。</summary>
        private bool HitTestLinkSegment(Vector2 mouse, Rect baseRect, float timeX0, out int segNote, out int segIdx, out bool isActive)
        {
            segNote = -1; segIdx = -1; isActive = false;
            float tol = 9f;
            // 1) 当前正在编辑的活动链
            if (linkingActive && linkTimes.Count >= 2)
            {
                for (int i = 0; i < linkTimes.Count - 1; i++)
                {
                    float x1 = timeX0 + (linkTimes[i] - viewStartTime) * pixelsPerSecond;
                    float y1 = GetNoteY(baseRect, linkLanes[i], linkLaneSpans != null && i < linkLaneSpans.Count && linkLaneSpans[i] > 1);
                    float x2 = timeX0 + (linkTimes[i + 1] - viewStartTime) * pixelsPerSecond;
                    float y2 = GetNoteY(baseRect, linkLanes[i + 1], linkLaneSpans != null && i + 1 < linkLaneSpans.Count && linkLaneSpans[i + 1] > 1);
                    if (DistToSeg(mouse, new Vector2(x1, y1), new Vector2(x2, y2)) <= tol)
                    { isActive = true; segIdx = i; return true; }
                }
            }
            // 2) 已完成的复节点音符（Hold / Linked 多节点）
            for (int n = 0; n < notes.Count; n++)
            {
                var note = notes[n];
                if (note.holdTimes == null || note.holdTimes.Length < 2) continue;
                bool linked = note.type == NoteData.NoteType.Linked;
                for (int i = 0; i < note.holdTimes.Length - 1; i++)
                {
                    float x1 = timeX0 + (note.holdTimes[i] - viewStartTime) * pixelsPerSecond;
                    float y1 = GetNoteY(baseRect, note.holdLanes[i], linked);
                    float x2 = timeX0 + (note.holdTimes[i + 1] - viewStartTime) * pixelsPerSecond;
                    float y2 = GetNoteY(baseRect, note.holdLanes[i + 1], linked);
                    if (DistToSeg(mouse, new Vector2(x1, y1), new Vector2(x2, y2)) <= tol)
                    { segNote = n; segIdx = i; return true; }
                }
            }
            return false;
        }

        private static float DistToSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp(Vector2.Dot(p - a, ab) / (ab.sqrMagnitude + 1e-6f), 0f, 1f);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }

        /// <summary>活动链在段 segIdx（节点 segIdx 与 segIdx+1 之间）断开：前后各自收尾成独立音符（均保留）。
        /// 类型按节点数决定：单节点保持原属性（点击/连轨），>=2 节点才是按住音符。</summary>
        private void BreakActiveChainAt(int segIdx)
        {
            if (!linkingActive || linkTimes.Count < 2) { FinishLinkingChain(); return; }
            int total = linkTimes.Count;
            if (segIdx < 0 || segIdx >= total - 1) { FinishLinkingChain(); return; }
            PushUndo();
            var firstTimes = linkTimes.GetRange(0, segIdx + 1);
            var firstLanes = linkLanes.GetRange(0, segIdx + 1);
            var firstSpans = linkLaneSpans.GetRange(0, segIdx + 1);
            var secTimes = linkTimes.GetRange(segIdx + 1, total - (segIdx + 1));
            var secLanes = linkLanes.GetRange(segIdx + 1, total - (segIdx + 1));
            var secSpans = linkLaneSpans.GetRange(segIdx + 1, total - (segIdx + 1));

            var first = MakeChainNote(firstTimes, firstLanes, firstSpans, NoteData.NoteType.Hold);
            var sec = MakeChainNote(secTimes, secLanes, secSpans, NoteData.NoteType.Hold);
            notes.Add(first);
            notes.Add(sec);
            selectedIndex = notes.Count - 2;
            linkingActive = false;
            linkLastIndex = -1;
            linkTimes.Clear(); linkLanes.Clear(); linkLaneSpans.Clear();
        }

        /// <summary>把一条已完成的复节点音符在段 segIdx 处断开：拆成前后两条独立音符（均保留，仅移除中间那根链接线）。
        /// 拆分后按"该段节点数"决定类型：单节点保持原属性（点击=Tap / 连轨=Linked，span 不变），>=2 节点才是按住音符(Hold/Linked)。</summary>
        private void SplitHoldNoteAt(int noteIndex, int segIdx)
        {
            if (noteIndex < 0 || noteIndex >= notes.Count) return;
            var note = notes[noteIndex];
            if (note.holdTimes == null || note.holdTimes.Length < 2) return;
            // 规整 holdLaneSpans（缺省按 1），保证拆分时连轨属性不丢失
            if (note.holdLaneSpans == null || note.holdLaneSpans.Length != note.holdTimes.Length)
            {
                note.holdLaneSpans = new int[note.holdTimes.Length];
                for (int i = 0; i < note.holdTimes.Length; i++) note.holdLaneSpans[i] = 1;
            }
            int len = note.holdTimes.Length;
            if (segIdx < 0 || segIdx >= len - 1) return;
            PushUndo();
            var firstTimes = new List<float>(note.holdTimes).GetRange(0, segIdx + 1);
            var firstLanes = new List<int>(note.holdLanes).GetRange(0, segIdx + 1);
            var firstSpans = new List<int>(note.holdLaneSpans).GetRange(0, segIdx + 1);
            var secTimes = new List<float>(note.holdTimes).GetRange(segIdx + 1, len - (segIdx + 1));
            var secLanes = new List<int>(note.holdLanes).GetRange(segIdx + 1, len - (segIdx + 1));
            var secSpans = new List<int>(note.holdLaneSpans).GetRange(segIdx + 1, len - (segIdx + 1));

            // 前段写回原音符（复用引用，避免重复 Remove/Insert）
            var first = MakeChainNote(firstTimes, firstLanes, firstSpans, note.type);
            note.time = first.time;
            note.lane = first.lane;
            note.type = first.type;
            note.holdTimes = first.holdTimes;
            note.holdLanes = first.holdLanes;
            note.holdLaneSpans = first.holdLaneSpans;
            note.holdEndLane = first.holdEndLane;
            note.holdDuration = first.holdDuration;

            // 后段作为新音符插入
            var sec = MakeChainNote(secTimes, secLanes, secSpans, note.type);
            notes.Insert(noteIndex + 1, sec);
            selectedIndex = noteIndex;
        }

        private static bool Near(Vector2 mouse, float x, float y, float yTolerance = 18f)
        {
            return Mathf.Abs(mouse.x - x) <= 8f && Mathf.Abs(mouse.y - y) <= yTolerance;
        }

        private static float GetNoteY(Rect baseRect, int lane, bool linked)
        {
            int clampedLane = Mathf.Clamp(lane, 0, linked ? 2 : 3);
            float displayRow = 3f - clampedLane;
            // Linked 的第一轨与下一轨之间，中心点位于两条轨道中心的正中间。
            float laneCenter = linked ? displayRow : displayRow + 0.5f;
            return baseRect.y + MarkerZoneHeight + RulerHeight + laneCenter * LaneHeight;
        }

        /// <summary>
        /// 节点 k 是否为连轨（2 轨宽）。优先用逐节点 holdLaneSpans；
        /// 未记录时按整条 type 推断（Linked => 全 2 轨），以兼容旧谱面。
        /// </summary>
        private static bool NodeIsLinked(ChartNote n, int k)
        {
            if (n.holdLaneSpans != null && k >= 0 && k < n.holdLaneSpans.Length)
                return n.holdLaneSpans[k] > 1;
            return n.type == NoteData.NoteType.Linked;
        }

        private static void DrawRoundedRect(Rect rect, float radius, Color color)
        {
            radius = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (radius <= 0.01f)
            {
                EditorGUI.DrawRect(rect, color);
                return;
            }

            var points = new Vector3[12];
            Vector2[] centers =
            {
                new Vector2(rect.xMax - radius, rect.yMax - radius),
                new Vector2(rect.xMin + radius, rect.yMax - radius),
                new Vector2(rect.xMin + radius, rect.yMin + radius),
                new Vector2(rect.xMax - radius, rect.yMin + radius)
            };
            int p = 0;
            for (int corner = 0; corner < centers.Length; corner++)
            {
                float start = corner * 90f;
                for (int i = 0; i < 3; i++)
                {
                    float angle = (start + i * 45f) * Mathf.Deg2Rad;
                    points[p++] = centers[corner] + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
            }

            Handles.color = color;
            Handles.DrawAAConvexPolygon(points);
        }

        /// <summary>清空链接/点链编辑状态（两模式切换互斥时调用）</summary>
        private void ResetLinkState()
        {
            linkingActive = false;
            linkLastIndex = -1;
            linkTimes.Clear();
            linkLanes.Clear();
            linkLaneSpans.Clear();
            pendingLinkDown = false;
            pointLinkSource = -1;
            pointLinkDragging = false;
        }

        // ---------- 合并 / 拆分 / 缩放 辅助 ----------
        /// <summary>由节点列表构造一个音符；类型按节点数决定：单节点保持原属性（点击=Tap / 连轨=Linked，span 决定），多节点才是按住音符(originalType)。</summary>
        private ChartNote MakeChainNote(List<float> times, List<int> lanes, List<int> spans, NoteData.NoteType originalType)
        {
            int count = times.Count;
            var n = new ChartNote();
            n.holdTimes = times.ToArray();
            n.holdLanes = lanes.ToArray();
            n.holdLaneSpans = spans.ToArray();
            n.time = times[0];
            n.lane = lanes[0];
            n.holdEndLane = lanes[count - 1];
            n.holdDuration = (count == 1) ? 0f : Mathf.Max(0.1f, times[count - 1] - times[0]);
            n.type = (count == 1)
                ? (spans[0] == 2 ? NoteData.NoteType.Linked : NoteData.NoteType.Tap)
                : originalType;
            return n;
        }

        /// <summary>把音符 A 与 B 合并为一条按住音符（纯链接：各自节点属性/连轨宽度原样保留，仅按时间排序串成链）。</summary>
        private void MergeNotesIntoChain(int aIdx, int bIdx)
        {
            if (aIdx < 0 || bIdx < 0 || aIdx >= notes.Count || bIdx >= notes.Count) return;
            if (aIdx == bIdx) return;
            PushUndo();
            var times = new List<float>();
            var lanes = new List<int>();
            var spans = new List<int>();
            AppendNoteNodes(times, lanes, spans, notes[aIdx]);
            AppendNoteNodes(times, lanes, spans, notes[bIdx]);
            SortTriple(times, lanes, spans); // 按时间排序，A→B 或 B→A 都保持正确时序

            var merged = MakeChainNote(times, lanes, spans, NoteData.NoteType.Hold);
            int hi = Mathf.Max(aIdx, bIdx);
            int lo = Mathf.Min(aIdx, bIdx);
            notes.RemoveAt(hi);
            notes.RemoveAt(lo);
            notes.Insert(lo, merged);
            selectedIndex = lo;
            AutoExtendSongLength();
            Repaint();
        }

        /// <summary>把一个音符的所有节点（含 span）展开进列表；单节点按类型推断 span（连轨=2 / 点击=1），不改变音符自身属性。</summary>
        private void AppendNoteNodes(List<float> times, List<int> lanes, List<int> spans, ChartNote n)
        {
            if (n.holdTimes != null && n.holdTimes.Length >= 2)
            {
                int[] sp = n.holdLaneSpans;
                if (sp == null || sp.Length != n.holdTimes.Length)
                {
                    sp = new int[n.holdTimes.Length];
                    for (int i = 0; i < sp.Length; i++) sp[i] = 1;
                }
                for (int i = 0; i < n.holdTimes.Length; i++)
                {
                    times.Add(n.holdTimes[i]);
                    lanes.Add(n.holdLanes[i]);
                    spans.Add(sp[i]);
                }
            }
            else
            {
                int span = 1;
                if (n.holdLaneSpans != null && n.holdLaneSpans.Length > 0) span = n.holdLaneSpans[0];
                else if (n.type == NoteData.NoteType.Linked) span = 2;
                times.Add(n.time);
                lanes.Add(n.lane);
                spans.Add(span);
            }
        }

        private static void SortTriple(List<float> times, List<int> lanes, List<int> spans)
        {
            var idx = new List<int>();
            for (int i = 0; i < times.Count; i++) idx.Add(i);
            idx.Sort((x, y) => times[x].CompareTo(times[y]));
            var nt = new List<float>(); var nl = new List<int>(); var ns = new List<int>();
            foreach (var i in idx) { nt.Add(times[i]); nl.Add(lanes[i]); ns.Add(spans[i]); }
            times.Clear(); lanes.Clear(); spans.Clear();
            times.AddRange(nt); lanes.AddRange(nl); spans.AddRange(ns);
        }

        /// <summary>改 BPM 时等比缩放：全部音符时间点 + 全部标记时间点按 factor 同比例缩放，保持彼此一致并重新对齐节拍网格。</summary>
        private void RescaleChart(float factor)
        {
            if (Mathf.Approximately(factor, 1f)) return;
            PushUndo();
            for (int i = 0; i < notes.Count; i++)
            {
                var n = notes[i];
                n.time *= factor;
                if (n.holdTimes != null)
                    for (int k = 0; k < n.holdTimes.Length; k++) n.holdTimes[k] *= factor;
                if (n.holdDuration > 0f) n.holdDuration *= factor;
            }
            for (int i = 0; i < markers.Count; i++) markers[i] *= factor;
            playTime = Mathf.Clamp(playTime * factor, 0f, float.MaxValue);
        }

        /// <summary>仅命时间轴正上方「标记条」（MarkerZoneHeight）内的三角旗标，用于左键选中 / 右键删除；X 方向 ±12px，整条带高度都可点，避免与轨道加音符冲突。</summary>
        private int HitTestMarkerTriangle(Vector2 mouse, Rect baseRect, float timeX0)
        {
            for (int i = markers.Count - 1; i >= 0; i--)
            {
                float x = timeX0 + (markers[i] - viewStartTime) * pixelsPerSecond;
                if (mouse.y >= baseRect.y && mouse.y <= baseRect.y + MarkerZoneHeight &&
                    Mathf.Abs(mouse.x - x) <= 12f) return i;
            }
            return -1;
        }

        private static void DrawChainTapCount(Rect rect, int count)
        {
            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };
            style.normal.textColor = Color.black;
            GUI.Label(rect, Mathf.Clamp(count, 3, 10).ToString(), style);
        }

        private static void DrawThickLine(Vector2 a, Vector2 b, float thickness, Color col)
        {
            Vector2 dir = b - a;
            float len = dir.magnitude;
            if (len < 0.001f) return;
            float ang = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            Matrix4x4 m = GUI.matrix;
            GUIUtility.RotateAroundPivot(ang, a);
            EditorGUI.DrawRect(new Rect(a.x, a.y - thickness * 0.5f, len, thickness), col);
            GUI.matrix = m;
        }

        private static void DrawLinkedStair(Vector2 a, Vector2 b, float thickness, Color col)
        {
            float dx = b.x - a.x;
            if (Mathf.Abs(dx) < 0.001f)
            {
                EditorGUI.DrawRect(new Rect(a.x - thickness * 0.5f, a.y - thickness * 0.5f,
                    thickness, thickness), col);
                return;
            }

            // 每个小段保持水平，只逐段改变 Y，和运行时的跨轨阶梯带保持一致。
            float direction = Mathf.Sign(dx);
            float startX = a.x + direction * 6f;
            float endX = b.x - direction * 6f;
            float usableWidth = Mathf.Abs(endX - startX);
            if (usableWidth <= 1f)
            {
                EditorGUI.DrawRect(new Rect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y) - thickness * 0.5f,
                    Mathf.Max(1f, Mathf.Abs(dx)), thickness), col);
                return;
            }

            int segmentCount = Mathf.Clamp(Mathf.CeilToInt(usableWidth / 10f), 2, 32);
            float segmentWidth = usableWidth / segmentCount * 1.12f;
            for (int i = 0; i < segmentCount; i++)
            {
                float p = (i + 0.5f) / segmentCount;
                float x = Mathf.Lerp(startX, endX, p);
                float y = Mathf.Lerp(a.y, b.y, p);
                EditorGUI.DrawRect(new Rect(x - segmentWidth * 0.5f, y - thickness * 0.5f,
                    segmentWidth, thickness), col);
            }
        }

        /// <summary>在轨道时间轴正下方绘制音乐素材波形图，与 time 轴对齐、跟随滚动/播放头。仅当有音乐素材时由 DrawTimeline 调用。</summary>
        private void DrawWaveform(Rect wfRect)
        {
            EditorGUI.DrawRect(wfRect, new Color(0.05f, 0.05f, 0.07f));
            float[] samples = GetCachedSamples(beatmapAudioClip);
            if (samples == null) return;

            float timeX0 = wfRect.x + LaneLabelWidth;
            float visibleWidth = wfRect.width - LaneLabelWidth;
            if (visibleWidth < 1f) return;
            float samplesPerSecond = samples.Length / Mathf.Max(0.001f, beatmapAudioClip.length);

            int w = Mathf.FloorToInt(visibleWidth);
            for (int px = 0; px < w; px++)
            {
                float t0 = viewStartTime + (px / pixelsPerSecond);
                float t1 = viewStartTime + ((px + 1) / pixelsPerSecond);
                int s0 = Mathf.Clamp(Mathf.FloorToInt(t0 * samplesPerSecond), 0, samples.Length - 1);
                int s1 = Mathf.Clamp(Mathf.FloorToInt(t1 * samplesPerSecond), 0, samples.Length - 1);
                float max = 0f;
                int span = s1 - s0;
                int step = Mathf.Max(1, span / 4); // 限幅：每像素最多扫 ~4 个样本，避免长音频每帧卡顿
                for (int s = s0; s <= s1; s += step)
                {
                    float v = Mathf.Abs(samples[s]);
                    if (v > max) max = v;
                }
                float x = timeX0 + px;
                float barH = max * wfRect.height * 0.45f;
                if (barH > 0.5f)
                    EditorGUI.DrawRect(new Rect(x, wfRect.y + wfRect.height * 0.5f - barH, 1f, barH * 2f), new Color(0.4f, 0.8f, 1f, 0.9f));
            }

            // 播放头（与轨道区共用 viewStartTime / playTime，红色对齐）
            float phx = timeX0 + (playTime - viewStartTime) * pixelsPerSecond;
            EditorGUI.DrawRect(new Rect(phx, wfRect.y, 1f, wfRect.height), Color.red);
        }

        /// <summary>读取并缓存 AudioClip 的混音单声道样本；首次访问解 PCM，之后走缓存。失败返回 null。</summary>
        private float[] GetCachedSamples(AudioClip clip)
        {
            if (clip == null) return null;
            if (_waveCache.TryGetValue(clip, out var cached) && cached != null) return cached;
            try
            {
                int frames = clip.samples;     // 每声道样本数
                int ch = clip.channels;
                if (frames <= 0 || ch <= 0) return null;
                if (!clip.preloadAudioData && clip.loadState != AudioDataLoadState.Loaded)
                    clip.LoadAudioData();
                float[] raw = new float[frames * ch];
                if (!clip.GetData(raw, 0)) return null;
                float[] mono = new float[frames];
                for (int f = 0; f < frames; f++)
                {
                    float s = 0f;
                    for (int c = 0; c < ch; c++) s += raw[f * ch + c];
                    mono[f] = s / ch;
                }
                _waveCache[clip] = mono;
                return mono;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[谱面编辑器] 波形读取失败：{e.Message}");
                return null;
            }
        }

        private float Snap(float time)
        {
            float secPerBeat = 60f / Mathf.Max(0.001f, bpm);
            return Mathf.Round(time / secPerBeat) * secPerBeat;
        }

        // ===================================================================
        // 撤销
        // ===================================================================
        private static ChartNote CloneNote(ChartNote src)
        {
            if (src == null) return null;
            return new ChartNote
            {
                time = src.time,
                lane = src.lane,
                type = src.type,
                isSmallTap = src.isSmallTap,
                chainTapCount = src.type == NoteData.NoteType.ChainTap
                    ? Mathf.Clamp(src.chainTapCount, 3, 10)
                    : src.chainTapCount,
                holdDuration = src.holdDuration,
                holdEndLane = src.holdEndLane,
                holdTimes = src.holdTimes == null ? null : (float[])src.holdTimes.Clone(),
                holdLanes = src.holdLanes == null ? null : (int[])src.holdLanes.Clone(),
                holdLaneSpans = src.holdLaneSpans == null ? null : (int[])src.holdLaneSpans.Clone()
            };
        }

        private static List<ChartNote> CloneNotes(List<ChartNote> src)
        {
            var dst = new List<ChartNote>(src.Count);
            foreach (var n in src) dst.Add(CloneNote(n));
            return dst;
        }

        /// <summary>
        /// 在当前编辑操作“之前”调用：把 notes 的完整快照压入撤销栈。
        /// </summary>
        private void PushUndo()
        {
            undoStack.Add(CloneNotes(notes));
            if (undoStack.Count > UndoCap)
                undoStack.RemoveAt(0); // 超出上限丢弃最早的一步
        }

        /// <summary>
        /// 撤销：弹出最近一次快照并恢复；同时清理可能失效的链接编辑态。
        /// </summary>
        private void Undo()
        {
            if (undoStack.Count == 0)
            {
                ShowNotification(new GUIContent("没有可撤销的操作"));
                return;
            }
            notes = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            selectedIndex = -1;
            dragIndex = -1;
            dragNote = null;
            // 链接编辑态若指向已不存在的索引则复位，避免悬空引用
            if (linkingActive && (linkLastIndex < 0 || linkLastIndex >= notes.Count))
            {
                linkingActive = false;
                linkLastIndex = -1;
                linkTimes.Clear();
                linkLanes.Clear();
                linkLaneSpans.Clear();
            }
            else if (linkingActive && linkLastIndex >= 0 && linkLastIndex < notes.Count)
            {
                // 仍处于链接编辑态：把暂存的节点列表重新同步到“撤销后”的链音符，
                // 避免下一次落点基于过时的 linkTimes/linkLanes。
                var hn = notes[linkLastIndex];
                if (hn.holdLanes != null && hn.holdTimes != null && hn.holdLaneSpans != null &&
                    hn.holdLanes.Length >= 2 && hn.holdTimes.Length >= 2 && hn.holdLaneSpans.Length >= 2)
                {
                    linkTimes = new List<float>(hn.holdTimes);
                    linkLanes = new List<int>(hn.holdLanes);
                    linkLaneSpans = new List<int>(hn.holdLaneSpans);
                }
                else
                {
                    // 链已收尾为单音符：结束链接编辑态
                    linkingActive = false;
                    linkLastIndex = -1;
                    linkTimes.Clear();
                    linkLanes.Clear();
                    linkLaneSpans.Clear();
                }
            }
            Repaint();
        }

        /// <summary>
        /// 结束正在编辑的链接链：
        /// - 已落 >=2 个节点 → 保留为一条多节点 Hold（holdLanes/holdTimes/holdLaneSpans 已填好），
        ///   逐节点宽度由 holdLaneSpans 决定（普通节点 width=1，连轨节点 width=2）。
        /// - 仅落 1 个节点 → 按该节点自身宽度决定：width=2 保留为双轨点击(Linked)，否则普通点击(Tap)。
        /// 无论如何都清理链接编辑状态。每个节点的类型/宽度互不影响。
        /// </summary>
        private void FinishLinkingChain()
        {
            PushUndo(); // 结束链接（断开/收尾）前记录快照，撤销可恢复链接编辑态

            if (linkingActive && linkLastIndex >= 0 && linkLastIndex < notes.Count)
            {
                var hn = notes[linkLastIndex];
                if (linkTimes.Count <= 1)
                {
                    int span = linkLaneSpans.Count > 0 ? linkLaneSpans[0] : 1;
                    hn.type = span == 2 ? NoteData.NoteType.Linked : NoteData.NoteType.Tap;
                    hn.holdLanes = null;
                    hn.holdTimes = null;
                    hn.holdLaneSpans = null;
                    hn.holdDuration = 0f;
                    hn.holdEndLane = hn.lane;
                }
                else
                {
                    hn.type = NoteData.NoteType.Hold; // 多节点链 = 连续按住滑动
                    hn.holdLanes = linkLanes.ToArray();
                    hn.holdTimes = linkTimes.ToArray();
                    hn.holdLaneSpans = linkLaneSpans.ToArray();
                    hn.lane = linkLanes[0];
                    hn.holdEndLane = linkLanes[linkLanes.Count - 1];
                    hn.holdDuration = Mathf.Max(0.1f, linkTimes[linkTimes.Count - 1] - linkTimes[0]);
                }
                selectedIndex = linkLastIndex;
            }
            linkingActive = false;
            linkLastIndex = -1;
            linkTimes.Clear();
            linkLanes.Clear();
            linkLaneSpans.Clear();
        }

        /// <summary>
        /// 提交一次"左键按住"的链接落点（在 MouseUp 时调用）：
        /// - 若期间上下拖动 >=1 轨 => 本次落点为连轨音符（覆盖相邻两轨，width=2）。
        /// - 否则视为普通单轨节点（width=1）。
        /// 每个节点独立记录自己的宽度（linkLaneSpans），不再把整条链强制升级为 Linked，
        /// 因此第一个普通节点不会因为后面的连轨节点而跟着变成连轨。
        /// 提交后仍保持 linkingActive，继续自动链接鼠标；右键可断开（FinishLinkingChain）。
        /// </summary>
        private void CommitPendingLinkNode(Rect baseRect, float timeX0)
        {
            // 两模式均关闭：直接丢弃本次按下
            if (!linkingMode && !pointLinkMode) { pendingLinkDown = false; return; }

            PushUndo(); // 落下一个链接节点前记录快照，撤销可回退该节点

            // 按下时已做过失效恢复，这里再保险一次
            if (linkingActive && (linkLastIndex < 0 || linkLastIndex >= notes.Count))
            {
                linkingActive = false;
                linkLastIndex = -1;
                linkTimes.Clear();
                linkLanes.Clear();
                linkLaneSpans.Clear();
            }

            // 点链模式（H）：按下 A 并拖到 B 再松开 = 生成「A→B」2 节点 Hold
            // - 必须有明显横向拖动（>8px）才生成；否则视为误触、无操作
            Vector2 cur = Event.current.mousePosition;
            float dragDist = Vector2.Distance(cur, linkDownMouse);
            if (pointLinkMode && dragDist > 8f)
            {
                float tA = snapToBeat ? Snap(linkDownTime) : linkDownTime;
                float tB = (cur.x - timeX0) / pixelsPerSecond + viewStartTime;
                tB = snapToBeat ? Snap(tB) : tB;
                tB = Mathf.Clamp(tB, 0f, songLength);
                if (tB < tA + 0.1f) tB = tA + 0.1f; // 长按至少 0.1s，且不允许拖向过去
                int laneA = linkDownLane;
                int laneB = 3 - Mathf.Clamp(Mathf.FloorToInt((cur.y - baseRect.y - MarkerZoneHeight - RulerHeight) / LaneHeight), 0, 3);
                int dLane = Mathf.Clamp(laneB - laneA, -1, 1);
                bool cross = dLane != 0;
                int[] lanes = cross ? new int[] { laneA, laneB } : new int[] { laneA, laneA };
                int[] spans = cross ? new int[] { 2, 2 } : new int[] { 1, 1 };
                var hn = new ChartNote
                {
                    time = tA,
                    lane = laneA,
                    type = NoteData.NoteType.Hold,
                    holdLanes = lanes,
                    holdTimes = new float[] { tA, tB },
                    holdLaneSpans = spans,
                    holdEndLane = laneB,
                    holdDuration = Mathf.Max(0.1f, tB - tA)
                };
                notes.Add(hn);
                selectedIndex = notes.Count - 1;
                AutoExtendSongLength();
                pendingLinkDown = false;
                linkingActive = false;
                linkLastIndex = -1;
                linkTimes.Clear(); linkLanes.Clear(); linkLaneSpans.Clear();
                Repaint();
                return;
            }
            // 点链模式无明显拖动：不动作（避免与单击误触混淆）
            if (pointLinkMode)
            {
                pendingLinkDown = false;
                Repaint();
                return;
            }

            // 链点模式 (G)：单击 = 累加节点（多节点链）；保持当前"按住上下拖动生成连轨" 的语义

            // 当前指针所在轨道（用于判断拖动了几轨）
            float localY = Event.current.mousePosition.y - baseRect.y;
            int displayRow = Mathf.Clamp(Mathf.FloorToInt((localY - MarkerZoneHeight - RulerHeight) / LaneHeight), 0, 3);
            int endLane = 3 - displayRow;
            // 拖动方向钳制到相邻 1 轨：下拖(step<0) / 上拖(step>0)
            int step = Mathf.Clamp(endLane - linkDownLane, -1, 1);
            bool crossLane = step != 0;
            int span = crossLane ? 2 : 1; // 本节点自己的宽度
            // 连轨"上"轨：覆盖 nodeLane..nodeLane+1 两条相邻轨
            int nodeLane = crossLane
                ? Mathf.Clamp(Mathf.Min(linkDownLane, linkDownLane + step), 0, 2)
                : linkDownLane;

            float snapped = snapToBeat ? Snap(linkDownTime) : linkDownTime;
            // 限制：后续节点不能早于上一节点（谱面位置不可倒退）
            if (linkingActive && linkTimes.Count > 0 && snapped < linkTimes[linkTimes.Count - 1])
            {
                ShowNotification(new GUIContent("后续节点不能早于上一节点，已钳制到上一节点时刻"));
                snapped = linkTimes[linkTimes.Count - 1];
            }

            if (!linkingActive)
            {
                var hn = new ChartNote
                {
                    time = snapped,
                    lane = nodeLane,
                    // 单节点：width=2 => 连轨点击(Linked)，否则普通点击(Tap)
                    type = span == 2 ? NoteData.NoteType.Linked : NoteData.NoteType.Tap,
                    holdLanes = new int[] { nodeLane },
                    holdTimes = new float[] { snapped }
                };
                hn.holdLaneSpans = new int[] { span };
                notes.Add(hn);
                linkLastIndex = notes.Count - 1;
                linkTimes.Clear(); linkLanes.Clear(); linkLaneSpans.Clear();
                linkTimes.Add(snapped); linkLanes.Add(nodeLane); linkLaneSpans.Add(span);
                linkingActive = true;
            }
            else
            {
                linkTimes.Add(snapped);
                linkLanes.Add(nodeLane);
                linkLaneSpans.Add(span);
                var hn = notes[linkLastIndex];
                // 多节点链统一为 Hold（连续按住滑动）；逐节点宽度由 holdLaneSpans 决定，
                // 第一个普通节点(width=1)不会因为后续连轨节点(width=2)而变成连轨。
                hn.type = NoteData.NoteType.Hold;
                hn.holdLanes = linkLanes.ToArray();
                hn.holdTimes = linkTimes.ToArray();
                hn.holdLaneSpans = linkLaneSpans.ToArray();
                hn.lane = linkLanes[0];
                hn.holdEndLane = nodeLane;
                hn.holdDuration = Mathf.Max(0.1f, linkTimes[linkTimes.Count - 1] - linkTimes[0]);
            }
            selectedIndex = linkLastIndex;
            Repaint();
        }

        // ===================================================================
        // 播放预览
        // ===================================================================
        private void Play()
        {
            if (isPlaying) return;
            EnsurePreviewSource();
            if (previewSource != null && beatmapAudioClip != null)
            {
                previewSource.clip = beatmapAudioClip;
                previewSource.time = playTime;
                previewSource.pitch = playbackSpeed; // 音频随倍速同步变速
                previewSource.Play();
            }
            playStartEditorTime = EditorApplication.timeSinceStartup;
            playStartOffset = playTime;
            isPlaying = true;
        }

        /// <summary>切换播放倍速：重锚播放起点，使切换瞬间 playTime 不跳变；音频 pitch 同步。</summary>
        private void SetPlaybackSpeed(float s)
        {
            if (Mathf.Approximately(s, playbackSpeed)) return;
            playStartOffset = playTime;
            playStartEditorTime = EditorApplication.timeSinceStartup;
            playbackSpeed = s;
            if (previewSource != null) previewSource.pitch = s;
            Repaint();
        }

        private void Pause()
        {
            if (!isPlaying) return;
            isPlaying = false;
            if (previewSource != null && beatmapAudioClip != null) previewSource.Pause();
        }

        private void StopPlayback()
        {
            isPlaying = false;
            playTime = 0f;
            playStartOffset = 0f;
            StopPreview();
            Repaint();
        }

        private void StopPreview()
        {
            if (previewSource != null && beatmapAudioClip != null)
            {
                try { previewSource.Stop(); } catch { }
            }
        }

        private void EnsurePreviewSource()
        {
            if (previewSource == null)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("BeatmapPreviewAudio", HideFlags.HideAndDontSave);
                previewSource = go.AddComponent<AudioSource>();
            }
        }

        // ===================================================================
        // 谱面仓库
        // ===================================================================
        private void DrawLibrary()
        {
            EditorGUILayout.LabelField("谱面仓库", EditorStyles.boldLabel);

            // 快捷：一键生成随机测试谱面并设为当前，方便随时切回随机谱面
            if (GUILayout.Button("生成随机测试谱面（并调用）", GUILayout.Height(24)))
            {
                DemoBeatmapGenerator.CreateDemoBeatmapWithBeats(120, DemoBeatmapGenerator.Density.Medium);
                SetActiveBeatmap($"{BeatmapsDir}/DemoBeatmap.asset");
                ShowNotification(new GUIContent("已生成随机测试谱面并设为当前谱面"));
            }

            // 取消调用：清空标记，下次搭建场景回退到 Demo 谱面
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("取消调用（恢复 Demo 谱面）", GUILayout.Height(24)))
            {
                EditorPrefs.DeleteKey(ActiveBeatmapKey);
                ShowNotification(new GUIContent("已取消调用，下次搭建将使用 Demo 谱面"));
                Repaint();
            }
            string activePath = EditorPrefs.GetString(ActiveBeatmapKey, "");
            EditorGUILayout.LabelField(
                string.IsNullOrEmpty(activePath) ? "当前：未调用（Demo 谱面）" : "当前调用中：" + Path.GetFileNameWithoutExtension(activePath),
                GUILayout.Height(24));
            EditorGUILayout.EndHorizontal();

            if (!AssetDatabase.IsValidFolder(BeatmapsDir))
            {
                EditorGUILayout.HelpBox("仓库为空（尚无保存的谱面）。保存后会自动创建 Assets/Beatmaps 目录。", MessageType.Info);
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:BeatmapSO", new[] { BeatmapsDir });
            libScroll = EditorGUILayout.BeginScrollView(libScroll, GUILayout.Height(170));
            foreach (var g in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(g);
                var bm = AssetDatabase.LoadAssetAtPath<BeatmapSO>(path);
                if (bm == null) continue;

                int side0 = bm.notes != null ? bm.notes.Count(n => n.side == 0) : 0;
                int jsonSide0 = CountSide0InJson(GetJsonPath(path));
                bool damaged = jsonSide0 > side0 + 1; // 文本备份比资产多 => 资产被截断
                bool active = (path == activePath);
                string name = Path.GetFileNameWithoutExtension(path);
                bool hasMusic = bm.audioClip != null;

                Color prevColor = GUI.color;
                if (damaged) GUI.color = Color.red;
                EditorGUILayout.BeginHorizontal(active ? EditorStyles.helpBox : GUIStyle.none);
                EditorGUILayout.LabelField(
                    $"{(active ? "★ " : "")}{name}  ({side0} 音符, BPM {bm.bpm:F0}){(hasMusic ? "  [音乐]" : "")}{(damaged ? "  ⚠已损坏" : "")}",
                    GUILayout.Width(damaged ? 360 : 320));
                if (GUILayout.Button("编辑", GUILayout.Width(50))) LoadBeatmap(path);
                if (GUILayout.Button("调用", GUILayout.Width(50))) SetActiveBeatmap(path);
                if (damaged && GUILayout.Button("从文本恢复", GUILayout.Width(80))) RestoreFromText(path);
                if (GUILayout.Button("删除", GUILayout.Width(50)))
                {
                    if (EditorUtility.DisplayDialog("确认删除", $"确定从仓库删除谱面「{name}」？此操作不可撤销。", "删除", "取消"))
                    {
                        if (path == activePath) EditorPrefs.DeleteKey(ActiveBeatmapKey);
                        AssetDatabase.DeleteAsset(path);
                        AssetDatabase.Refresh();
                        ShowNotification(new GUIContent("已删除：" + name));
                    }
                }
                EditorGUILayout.EndHorizontal();
                if (damaged) GUI.color = prevColor;
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 把指定谱面设为「游戏启动谱面」。抽成独立方法以避免按钮 lambda 闭包捕获带来的
        /// 写入时序问题（已发现：连续调用不同谱面时第二次写入偶尔不落盘）。
        /// </summary>
        private void SetActiveBeatmap(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            EditorPrefs.SetString(ActiveBeatmapKey, path);

            // 直接切换场景中所有 NoteSpawner 的谱面引用并重置，使「调用」无需 Setup Demo Scene 即生效（不侵入 NoteSpawner.cs）。
            var bm = AssetDatabase.LoadAssetAtPath<BeatmapSO>(path);
            int switched = 0;
            if (bm != null)
            {
                var spawners = Object.FindObjectsByType<NoteSpawner>(FindObjectsSortMode.None);
                foreach (var sp in spawners)
                {
                    if (sp == null) continue;
                    sp.beatmap = bm;
                    sp.ResetSpawner();
                    switched++;
                }
            }
            Debug.Log($"[谱面编辑器] 已调用谱面：{path}" +
                (bm != null ? $"（已切换 {switched} 个场景 NoteSpawner）" : "（未找到谱面资产，仅更新调用标记）"));
            ShowNotification(new GUIContent("已调用：" + Path.GetFileNameWithoutExtension(path) +
                (switched > 0 ? $"（场景 {switched} 个生成器已切换）" : "")));
            Repaint();
        }

        private void LoadBeatmap(string path)
        {
            var bm = AssetDatabase.LoadAssetAtPath<BeatmapSO>(path);
            if (bm == null) return;
            currentEditingPath = path;
            beatmapName = Path.GetFileNameWithoutExtension(path);
            bpm = bm.bpm;
            beatmapAudioClip = bm.audioClip;
            markers = (bm.markers != null) ? new List<float>(bm.markers) : new List<float>();
            // 从资产音符数组重建单边谱面（含连轨宽度还原）
            LoadNotesFromNoteData(bm.notes);
            // 自检：若资产丢失连轨宽度而文本备份有，提示从文本恢复（纵深防御）
            VerifyAndMaybeRecoverOnLoad(path);
        }

        /// <summary>
        /// 从一组 NoteData（单边 side==0）重建编辑器内部谱面。同时清零撤销 / 链接编辑态。
        /// 被 LoadBeatmap（从资产）与 RestoreFromText（从 JSON）共用。
        /// </summary>
        private void LoadNotesFromNoteData(NoteData[] arr)
        {
            notes = new List<ChartNote>();
            selectedIndex = -1;
            dragIndex = -1;
            dragNote = null;
            linkingActive = false;
            linkingLinked = false;
            linkLastIndex = -1;
            linkTimes.Clear();
            linkLanes.Clear();
            linkLaneSpans.Clear();
            pendingLinkDown = false;
            undoStack.Clear(); // 载入新谱面后清空旧撤销历史

            if (arr != null)
            {
                foreach (var n in arr)
                {
                    if (n.side != 0) continue;
                    var cn = new ChartNote
                    {
                        time = n.time,
                        lane = n.lane,
                        type = n.type,
                        chainTapCount = n.type == NoteData.NoteType.ChainTap
                            ? Mathf.Clamp(n.chainTapCount, 3, 10)
                            : n.chainTapCount,
                        holdDuration = n.holdDuration,
                        holdEndLane = n.holdEndLane
                    };
                    if (n.type == NoteData.NoteType.SmallTap) cn.isSmallTap = true;
                    if (n.holdLanes != null) cn.holdLanes = (int[])n.holdLanes.Clone();
                    if (n.holdTimes != null) cn.holdTimes = (float[])n.holdTimes.Clone();
                    if (n.holdLaneSpans != null) cn.holdLaneSpans = (int[])n.holdLaneSpans.Clone();
                    notes.Add(cn);
                }
            }
            if (notes.Count > 0) AutoExtendSongLength();
            playTime = 0f;
            Repaint();
        }

        /// <summary>返回与 .asset 同名的 .json 文本备份路径。</summary>
        private static string GetJsonPath(string assetPath)
        {
            return Path.ChangeExtension(assetPath, ".json");
        }

        /// <summary>读取同级 .json 文本备份中 side==0 的音符数（用于检测资产被截断）。解析失败返回 0。</summary>
        private static int CountSide0InJson(string jsonPath)
        {
            try
            {
                if (!File.Exists(jsonPath)) return 0;
                var dump = JsonUtility.FromJson<BeatmapTextDump>(File.ReadAllText(jsonPath));
                if (dump == null || dump.notes == null) return 0;
                int c = 0;
                foreach (var n in dump.notes) if (n.side == 0) c++;
                return c;
            }
            catch (System.Exception) { return 0; }
        }

        /// <summary>根据当前 notes / 音乐素材自动扩展 songLength：
        /// 1) 若已挂音乐素材，取 max(用户设置值, music.length)；
        /// 2) 再与「最后音符结束时刻 + 4s」取 max，保证所有音符都能在时间轴上画出来。
        /// 用于：BPM 提交后、songLength 字段提交后、加载谱面后，确保时间轴长度跟随内容变化。</summary>
        private void AutoExtendSongLength()
        {
            float target = songLength;
            if (beatmapAudioClip != null) target = Mathf.Max(target, beatmapAudioClip.length);
            if (notes != null && notes.Count > 0)
            {
                float lastEnd = 0f;
                for (int i = 0; i < notes.Count; i++)
                {
                    var cn = notes[i];
                    if (cn.holdTimes != null && cn.holdTimes.Length > 0)
                        lastEnd = Mathf.Max(lastEnd, cn.holdTimes[cn.holdTimes.Length - 1]);
                    else
                        lastEnd = Mathf.Max(lastEnd, cn.time);
                }
                target = Mathf.Max(target, lastEnd + 4f);
            }
            if (!Mathf.Approximately(target, songLength))
            {
                songLength = target;
                Repaint();
            }
        }

        /// <summary>把已保存的 BeatmapSO 同步导出为同级 .json 文本镜像（纵深防御 / 可 git 提交）。</summary>
        private void ExportBeatmapText(BeatmapSO asset, string assetPath)
        {
            if (asset == null) return;
            try
            {
                string jsonPath = GetJsonPath(assetPath);
                var dump = new BeatmapTextDump { bpm = asset.bpm, notes = asset.notes, markers = asset.markers };
                File.WriteAllText(jsonPath, JsonUtility.ToJson(dump, true));
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[谱面编辑器] 导出文本备份失败：{e.Message}");
            }
        }

        /// <summary>从同级 .json 文本备份恢复编辑器谱面（含连轨宽度），并立即写回 .asset 固化。</summary>
        private void RestoreFromText(string assetPath)
        {
            string jsonPath = GetJsonPath(assetPath);
            if (!File.Exists(jsonPath))
            {
                EditorUtility.DisplayDialog("从文本恢复", "未找到同名的 .json 文本备份文件。", "确定");
                return;
            }
            string json = File.ReadAllText(jsonPath);
            BeatmapTextDump dump = JsonUtility.FromJson<BeatmapTextDump>(json);
            if (dump == null || dump.notes == null)
            {
                EditorUtility.DisplayDialog("从文本恢复", "文本备份解析失败或为空。", "确定");
                return;
            }
            bpm = dump.bpm;
            currentEditingPath = assetPath;
            beatmapName = Path.GetFileNameWithoutExtension(assetPath);
            markers = (dump.markers != null) ? new List<float>(dump.markers) : new List<float>();
            LoadNotesFromNoteData(dump.notes);
            SaveBeatmap(); // 立即把恢复出的连轨宽度固化进 .asset（与 .json 保持一致）
            ShowNotification(new GUIContent("已从文本恢复：" + Path.GetFileNameWithoutExtension(assetPath)));
        }

        /// <summary>
        /// 载入资产后自检：若资产中多节点 Hold 的逐节点宽度不完整，但同目录 .json 备份完整，
        /// 则提示用户从文本恢复（绝不静默丢数据）。
        /// </summary>
        private void VerifyAndMaybeRecoverOnLoad(string assetPath)
        {
            string jsonPath = GetJsonPath(assetPath);
            if (!File.Exists(jsonPath)) return;
            int jsonSide0 = CountSide0InJson(jsonPath);

            // 1) 连轨宽度丢失自检（仅多节点链）
            bool spanLost = false;
            foreach (var cn in notes)
            {
                if (cn.holdLanes == null || cn.holdLanes.Length < 2) continue; // 仅检查多节点链
                // 仅检查「长度完整性」：spans 数组存在且与节点数一致即视为正常。
                // 普通多节点 Hold 的 spans 全为 1（无连轨节点）也是合法状态，不能误判为损坏（修复 2026-08-27 误报弹窗）。
                bool spanOk = cn.holdLaneSpans != null
                    && cn.holdLaneSpans.Length == cn.holdLanes.Length;
                if (!spanOk) { spanLost = true; break; }
            }

            // 2) 截断自检：资产音符数远少于文本备份（"运行后变 1 音符"的根因表现）。
            // 注意：notes 在此只装 side==0（LoadNotesFromNoteData 已过滤），故 notes.Count 即资产 side0 数。
            bool noteLost = jsonSide0 > notes.Count + 1;

            if ((spanLost || noteLost) &&
                EditorUtility.DisplayDialog("检测到谱面可能损坏",
                    $"当前资产音符数（{notes.Count}）与文本备份（{jsonSide0}）不一致，部分音符可能丢失。是否从文本备份恢复？\n（恢复后重新保存即可固化）",
                    "从文本恢复", "忽略"))
            {
                RestoreFromText(assetPath);
            }
        }

        private void NewChart()
        {
            if (notes.Count > 0 &&
                !EditorUtility.DisplayDialog("新建谱面", "当前谱面尚未保存，确定新建空白谱面？", "新建", "取消"))
            {
                return;
            }
            notes = new List<ChartNote>();
            undoStack.Clear(); // 新建后没有可撤销的历史
            beatmapName = "NewBeatmap";
            currentEditingPath = "";
            beatmapAudioClip = null;
            markers = new List<float>();
            selectedIndex = -1;
            dragIndex = -1;
            dragNote = null;
            linkingActive = false;
            linkingLinked = false;
            linkLastIndex = -1;
            linkTimes.Clear();
            linkLanes.Clear();
            linkLaneSpans.Clear();
            pendingLinkDown = false;
            playTime = 0f;
            Repaint();
        }

        private void SaveBeatmap()
        {
            // 保存前若有未结束的链接链，先固化（避免半条链被保存）
            if (linkingActive) FinishLinkingChain();
            pendingLinkDown = false;

            if (string.IsNullOrWhiteSpace(beatmapName))
            {
                EditorUtility.DisplayDialog("提示", "请先填写谱面名称。", "确定");
                return;
            }
            EnsureFolder(BeatmapsDir);

            string safe = Sanitize(beatmapName);
            string path = string.IsNullOrEmpty(currentEditingPath)
                ? $"{BeatmapsDir}/{safe}.asset"
                : currentEditingPath;

            BeatmapSO asset = AssetDatabase.LoadAssetAtPath<BeatmapSO>(path);
            bool created = false;
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<BeatmapSO>();
                created = true;
            }

            // 截断保护（治本，2026-08-27）：若本次保存的音符数远低于谱面现有音符数，
            // 判定为编辑器内部缓冲区残缺，拒绝覆盖健康资产——直接阻断"运行后谱面变 1 音符"的根因。
            int existingSide0 = (!created && asset.notes != null) ? asset.notes.Count(n => n.side == 0) : 0;
            NoteData[] newNotes = BuildBothSideNotes();
            if (!created && existingSide0 > 0)
            {
                int newSide0 = newNotes.Count(n => n.side == 0);
                if (newSide0 < existingSide0 * 0.5f)
                {
                    Debug.LogError($"[谱面编辑器] 截断保护触发：本次保存音符数（{newSide0}）远低于现有（{existingSide0}），疑似编辑器缓冲区残缺，已阻止覆盖，谱面未改动。");
                    EditorUtility.DisplayDialog("保存被阻止",
                        $"本次保存的音符数（{newSide0}）远低于谱面现有音符数（{existingSide0}），疑似编辑器内部缓冲区残缺导致截断。已阻止覆盖以保护谱面。\n\n建议：检查编辑器内音符是否完整；若确已损坏，可点「从文本恢复」或「编辑」该谱面后从文本恢复。",
                        "确定");
                    return;
                }
            }

            asset.bpm = bpm;
            asset.audioClip = beatmapAudioClip;
            asset.notes = newNotes;
            asset.markers = markers.Count > 0 ? markers.ToArray() : null;

            if (created) AssetDatabase.CreateAsset(asset, path);
            else AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.SaveAssets();

            // 同步写出同级 .json 文本备份（连轨宽度纵深防御，可 git 提交）
            ExportBeatmapText(asset, path);

            AssetDatabase.Refresh();

            currentEditingPath = path;
            ShowNotification(new GUIContent("已保存：" + Path.GetFileNameWithoutExtension(path)));
            Repaint();
        }

        /// <summary>
        /// 把单边 4 轨谱面复制成 side=0 / side=1 两份（左右谱面完全相同），按时间升序排列。
        /// </summary>
        private NoteData[] BuildBothSideNotes()
        {
            var list = new List<NoteData>();
            foreach (var n in notes)
            {
                NoteData.NoteType type = n.isSmallTap ? NoteData.NoteType.SmallTap : n.type;
                int lane = type == NoteData.NoteType.Linked ? Mathf.Clamp(n.lane, 0, 2) : Mathf.Clamp(n.lane, 0, 3);
                int count = type == NoteData.NoteType.ChainTap ? Mathf.Clamp(n.chainTapCount, 3, 10) : n.chainTapCount;
                var nd0 = new NoteData { time = n.time, lane = lane, side = 0, type = type, chainTapCount = count };
                var nd1 = new NoteData { time = n.time, lane = lane, side = 1, type = type, chainTapCount = count };

                if (type == NoteData.NoteType.Hold || type == NoteData.NoteType.Linked)
                {
                    // Hold 与 Linked 长按都复用节点链；Linked 单节点没有数组，按双轨点击保存。
                    if (n.holdLanes != null && n.holdTimes != null && n.holdLanes.Length >= 2 && n.holdTimes.Length >= 2)
                    {
                        int maxLane = type == NoteData.NoteType.Linked ? 2 : 3;
                        int[] lanes = n.holdLanes.Select(value => Mathf.Clamp(value, 0, maxLane)).ToArray();
                        nd0.holdLanes = (int[])lanes.Clone();
                        nd1.holdLanes = (int[])lanes.Clone();
                        nd0.holdTimes = (float[])n.holdTimes.Clone();
                        nd1.holdTimes = (float[])n.holdTimes.Clone();
                        // 逐节点宽度（1=普通，2=连轨），与节点一一对应。
                        // 修复：绝不再因"长度不符"静默丢弃连轨信息——无条件写出归一化宽度
                        // （缺位补 1、超出截断；值 >1 即视为连轨宽节点）。
                        int[] spans = new int[lanes.Length];
                        if (n.holdLaneSpans != null)
                        {
                            for (int i = 0; i < lanes.Length; i++)
                                spans[i] = (i < n.holdLaneSpans.Length && n.holdLaneSpans[i] > 1) ? 2 : 1;
                        }
                        else
                        {
                            for (int i = 0; i < lanes.Length; i++) spans[i] = 1;
                        }
                        nd0.holdLaneSpans = (int[])spans.Clone();
                        nd1.holdLaneSpans = (int[])spans.Clone();
                    }
                    else if (type == NoteData.NoteType.Hold)
                    {
                        nd0.holdDuration = n.holdDuration;
                        nd1.holdDuration = n.holdDuration;
                        nd0.holdEndLane = n.holdEndLane;
                        nd1.holdEndLane = n.holdEndLane;
                    }
                }
                list.Add(nd0);
                list.Add(nd1);
            }
            list.Sort((a, b) => a.time.CompareTo(b.time));
            return list.ToArray();
        }

        private static string Sanitize(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            string s = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            s = s.Trim().Replace(" ", "_");
            if (string.IsNullOrEmpty(s)) s = "Beatmap";
            return s;
        }

        private static void EnsureFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
                string child = Path.GetFileName(folder);
                if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
