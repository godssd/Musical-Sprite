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
        [SerializeField] private bool snapToBeat = true;

        // ---------- 选择 / 拖拽 ----------
        private int selectedIndex = -1;
        private int dragIndex = -1;
        private ChartNote dragNote;
        private Vector2 dragStartMouse;
        private float dragStartNoteTime;
        private int dragStartNoteLane;

        // ---------- 链接模式（多次点击成链，可任意多节点 Hold） ----------
        [SerializeField] private bool linkingMode = false;
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
        private static readonly float WaveformHeight = 60f;

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
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
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
            playTime = (float)(EditorApplication.timeSinceStartup - playStartEditorTime) + playStartOffset;
            if (playTime > songLength)
            {
                StopPlayback();
            }
            Repaint();
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
            bpm = EditorGUILayout.FloatField("BPM", bpm, GUILayout.Width(120));
            songLength = EditorGUILayout.FloatField("歌曲长度(秒)", songLength, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            snapToBeat = EditorGUILayout.ToggleLeft("吸附到拍", snapToBeat, GUILayout.Width(90));
            linkingMode = EditorGUILayout.ToggleLeft("链接模式(点按成链)", linkingMode, GUILayout.Width(140));
            placeSmallTapMode = EditorGUILayout.ToggleLeft("小圈点击", placeSmallTapMode, GUILayout.Width(100));
            placeChainTapMode = EditorGUILayout.ToggleLeft("连点音符", placeChainTapMode, GUILayout.Width(90));
            if (placeChainTapMode)
            {
                int inputCount = EditorGUILayout.DelayedIntField("次数", Mathf.Clamp(chainTapCount, 3, 10), GUILayout.Width(110));
                chainTapCount = Mathf.Clamp(inputCount, 3, 10);
            }
            pixelsPerSecond = EditorGUILayout.Slider("缩放(像素/秒)", pixelsPerSecond, 10f, 240f, GUILayout.Width(240));
            beatmapAudioClip = (AudioClip)EditorGUILayout.ObjectField("音乐素材", beatmapAudioClip, typeof(AudioClip), false, GUILayout.Width(220));
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
            EditorGUILayout.LabelField($"音符数：{notes.Count}    当前时间：{playTime:F2}s", GUILayout.Width(220));
            EditorGUILayout.EndHorizontal();

            string activePath = EditorPrefs.GetString(ActiveBeatmapKey, "");
            string activeName = string.IsNullOrEmpty(activePath) ? "（未调用，使用 Demo 谱面）"
                : "★ 调用中：" + Path.GetFileNameWithoutExtension(activePath);
            EditorGUILayout.HelpBox(
                "音符的 time = 音符圆心抵达判定线的时刻（非发射时刻）；改难度只改移动速度，不影响该时刻。\n" +
                "普通音符：轨道区单击=加音符；拖拽=移动；右键/Delete=删除；点击刻度尺=定位播放头。\n" +
                "链接模式（按住点击音符）：勾选后左键落节点，右键结束；松开后仍自动链接鼠标，可继续落下一个节点（普通点击=单轨节点）。\n" +
                "连轨音符（链接模式专属）：左键按下不放并上下拖动 >=1 轨 => 制作连轨音符（覆盖拖动的相邻两轨），松开后同样自动链接鼠标；此时右键 => 变成普通的连轨点击音符。连轨不区分大小圈。普通点击仍可用「小圈点击」区分大小。\n" +
                "连点音符：勾选「连点音符」后单击生成普通大点击外形的连续点击音符；次数框手动输入 3-10 次，选中后可在上方修改。\n" +
                "挂上「音乐素材」后点播放可听音校谱；保存并「调用」后，下一次「搭建完整场景」将同步播放本谱面与音乐。\n" + activeName,
                MessageType.Info);
        }

        private void DrawTimeline()
        {
            Rect baseRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(RulerHeight + 4 * LaneHeight));

            // 背景
            EditorGUI.DrawRect(baseRect, new Color(0.08f, 0.08f, 0.10f));

            float timeX0 = baseRect.x + LaneLabelWidth;
            float visibleWidth = baseRect.width - LaneLabelWidth;
            float visibleSeconds = visibleWidth / pixelsPerSecond;
            if (visibleSeconds < 0.01f) visibleSeconds = 1f;

            // 滚动钳制
            float maxStart = Mathf.Max(0f, songLength - visibleSeconds);
            viewStartTime = Mathf.Clamp(viewStartTime, 0f, maxStart);

            // ---- 刻度尺 ----
            EditorGUI.DrawRect(new Rect(baseRect.x, baseRect.y, baseRect.width, RulerHeight), new Color(0.16f, 0.16f, 0.2f));
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
                EditorGUI.DrawRect(new Rect(x, baseRect.y + 4, 1, RulerHeight - 6), tickCol);
                if (major)
                {
                    GUI.Label(new Rect(x + 3, baseRect.y + 3, 70, 16), $"{b}拍", EditorStyles.miniLabel);
                }
            }

            // ---- 轨道 ----
            for (int lane = 0; lane < 4; lane++)
            {
                float y = baseRect.y + RulerHeight + (3 - lane) * LaneHeight;
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

            // 链接模式：左键按住 + 上下拖动 => 连轨音符（覆盖相邻两轨）实时预览
            if (linkingMode && pendingLinkDown)
            {
                Vector2 cur = Event.current.mousePosition;
                float localY = cur.y - baseRect.y;
                int displayRow = Mathf.Clamp(Mathf.FloorToInt((localY - RulerHeight) / LaneHeight), 0, 3);
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
                    float bandTop = baseRect.y + RulerHeight + (2 - commitLane) * LaneHeight;
                    EditorGUI.DrawRect(new Rect(baseRect.x, bandTop, baseRect.width, LaneHeight * 2f),
                        new Color(1f, 0.6f, 0.2f, 0.28f));
                }
                else
                {
                    // 单轨节点：仅高亮对应的一条轨道，避免预览比实际结果更宽
                    float bandTop = baseRect.y + RulerHeight + (3 - commitLane) * LaneHeight;
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
            EditorGUI.DrawRect(new Rect(phx, baseRect.y, 2, baseRect.height), Color.red);

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
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag &&
                e.type != EventType.MouseUp && e.type != EventType.KeyDown) return;
            if (e.type == EventType.KeyDown)
            {
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

            // 点击刻度尺 => 定位播放头
            if (localY < RulerHeight)
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
            int displayRow = Mathf.Clamp(Mathf.FloorToInt((localY - RulerHeight) / LaneHeight), 0, 3);
            int lane = 3 - displayRow;
            float time = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
            time = Mathf.Clamp(time, 0f, songLength);

            if (e.type == EventType.MouseDown)
            {
                int hit = HitTestNote(e.mousePosition, baseRect, timeX0);

            // 右键：链接编辑态中 = 断开链接（保留为普通/长按音符，不删除）；否则删除光标下音符
            if (e.button == 1)
            {
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

                    if (linkingMode)
                    {
                        if (e.button == 0)
                        {
                            // 记录一次左键按下，推迟到 MouseUp 决定落点类型：
                            //  - 仅点击（无上下拖动）=> 单轨节点（保持原有链接逻辑）
                            //  - 按住并上下拖动 >=1 轨 => 连轨音符（覆盖相邻两轨）
                            // 窗口热重载或删除正在编辑的链后，旧索引可能失效；直接从新链重新开始。
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
                    int dragDisplayRow = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y - baseRect.y - RulerHeight) / LaneHeight), 0, 3);
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
            return baseRect.y + RulerHeight + laneCenter * LaneHeight;
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
            if (!linkingMode) { pendingLinkDown = false; return; }

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

            // 当前指针所在轨道（用于判断拖动了几轨）
            float localY = Event.current.mousePosition.y - baseRect.y;
            int displayRow = Mathf.Clamp(Mathf.FloorToInt((localY - RulerHeight) / LaneHeight), 0, 3);
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
                previewSource.Play();
            }
            playStartEditorTime = EditorApplication.timeSinceStartup;
            playStartOffset = playTime;
            isPlaying = true;
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
            if (notes.Count > 0) songLength = Mathf.Max(songLength, notes.Max(cn => cn.time) + 4f);
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

        /// <summary>把已保存的 BeatmapSO 同步导出为同级 .json 文本镜像（纵深防御 / 可 git 提交）。</summary>
        private void ExportBeatmapText(BeatmapSO asset, string assetPath)
        {
            if (asset == null) return;
            try
            {
                string jsonPath = GetJsonPath(assetPath);
                var dump = new BeatmapTextDump { bpm = asset.bpm, notes = asset.notes };
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
                bool spanOk = cn.holdLaneSpans != null
                    && cn.holdLaneSpans.Length == cn.holdLanes.Length
                    && System.Linq.Enumerable.Any(cn.holdLaneSpans, v => v > 1);
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
