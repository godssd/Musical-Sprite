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
            public float holdDuration = 0f;   // 仅旧式 2 节点 Hold：持续时长（秒）
            public int holdEndLane = 0;       // 仅旧式 2 节点 Hold：结束音符所在轨道
            // 多节点 Hold：每个节点的时刻/轨道（length >= 2）。为空时退化成 2 节点 Hold。
            public float[] holdTimes;
            public int[] holdLanes;
        }

        [SerializeField] private string beatmapName = "NewBeatmap";
        [SerializeField] private float bpm = 128f;
        [SerializeField] private float songLength = 60f;
        [SerializeField] private List<ChartNote> notes = new List<ChartNote>();
        private string currentEditingPath = ""; // 正在编辑的资产路径；为空表示新谱面

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
        private int linkLastIndex = -1;                     // 正在编辑的 ChartNote 在 notes 中的索引（多节点 Hold 本体）

        // ---------- 小型点击音符创建 ----------
        [SerializeField] private bool placeSmallTapMode = false;

        // ---------- 播放预览 ----------
        private bool isPlaying;
        private double playStartEditorTime;
        private float playStartOffset;
        [SerializeField] private float playTime;
        // 谱面绑定的音乐素材（保存到 BeatmapSO.audioClip），编辑器预览与游戏运行时同步播放
        private AudioClip beatmapAudioClip;
        private AudioSource previewSource;

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
            placeSmallTapMode = EditorGUILayout.ToggleLeft("小型点击(单击生成)", placeSmallTapMode, GUILayout.Width(140));
            pixelsPerSecond = EditorGUILayout.Slider("缩放(像素/秒)", pixelsPerSecond, 10f, 240f, GUILayout.Width(240));
            beatmapAudioClip = (AudioClip)EditorGUILayout.ObjectField("音乐素材", beatmapAudioClip, typeof(AudioClip), false, GUILayout.Width(220));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("新建", GUILayout.Width(60))) NewChart();
            if (GUILayout.Button("保存", GUILayout.Width(60))) SaveBeatmap();
            if (!isPlaying)
            {
                if (GUILayout.Button("播放", GUILayout.Width(60))) Play();
            }
            else
            {
                if (GUILayout.Button("暂停", GUILayout.Width(60))) Pause();
            }
            if (GUILayout.Button("停止", GUILayout.Width(60))) StopPlayback();
            EditorGUILayout.LabelField($"音符数：{notes.Count}    当前时间：{playTime:F2}s", GUILayout.Width(220));
            EditorGUILayout.EndHorizontal();

            string activePath = EditorPrefs.GetString(ActiveBeatmapKey, "");
            string activeName = string.IsNullOrEmpty(activePath) ? "（未调用，使用 Demo 谱面）"
                : "★ 调用中：" + Path.GetFileNameWithoutExtension(activePath);
            EditorGUILayout.HelpBox(
                "音符的 time = 音符圆心抵达判定线的时刻（非发射时刻）；改难度只改移动速度，不影响该时刻。\n" +
                "普通音符：轨道区单击=加音符；拖拽=移动；右键/Delete=删除；点击刻度尺=定位播放头。\n" +
                "链接模式（按住点击音符）：勾选「链接模式」后，轨道区左键点击落下第 1 个音符，它会自动跟随鼠标；移动到新位置再次左键点击→与前一节点链接并继续跟随；可一直点击加节点（每两段链接算一次 CLEAR）。在「跟随阶段」点右键→当前链结束、已落节点固化为一条多节点长按。仅落 1 个节点时点右键→降级为普通点击音符。\n" +
                "小型点击音符：勾选「小型点击」后，在轨道区单击即可生成（紫色小方块，半径更小、命中统一 PASS）。\n" +
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
                float y = baseRect.y + RulerHeight + lane * LaneHeight;
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
                bool isHold = n.type == NoteData.NoteType.Hold;

                if (isHold)
                {
                    // 多节点 Hold：遍历所有节点画折线 + 每个节点画方块
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
                    for (int k = 0; k < ts.Length - 1; k++)
                    {
                        float xA = timeX0 + (ts[k] - viewStartTime) * pixelsPerSecond;
                        float yA = baseRect.y + RulerHeight + ls[k] * LaneHeight + LaneHeight / 2;
                        float xB = timeX0 + (ts[k + 1] - viewStartTime) * pixelsPerSecond;
                        float yB = baseRect.y + RulerHeight + ls[k + 1] * LaneHeight + LaneHeight / 2;
                        if (xB >= timeX0 - 16 && xA <= baseRect.x + baseRect.width + 16)
                            DrawThickLine(new Vector2(xA, yA), new Vector2(xB, yB), 4f, lineCol);
                    }
                    // 节点方块
                    for (int k = 0; k < ts.Length; k++)
                    {
                        float x = timeX0 + (ts[k] - viewStartTime) * pixelsPerSecond;
                        if (x < timeX0 - 16 || x > baseRect.x + baseRect.width + 16) continue;
                        float y = baseRect.y + RulerHeight + ls[k] * LaneHeight + LaneHeight / 2;
                        EditorGUI.DrawRect(new Rect(x - 6, y - 6, 12, 12),
                            (i == selectedIndex) ? Color.yellow : LaneColors[Mathf.Clamp(ls[k], 0, 3)]);
                    }
                }
                else
                {
                    float x = timeX0 + (n.time - viewStartTime) * pixelsPerSecond;
                    if (x < timeX0 - 16 || x > baseRect.x + baseRect.width + 16) continue;
                    float y = baseRect.y + RulerHeight + n.lane * LaneHeight + LaneHeight / 2;
                    Color c;
                    if (n.isSmallTap)
                        c = (i == selectedIndex) ? Color.yellow : new Color(0.7f, 0.4f, 1f); // 小型点击=紫色
                    else
                        c = (i == selectedIndex) ? Color.yellow : LaneColors[Mathf.Clamp(n.lane, 0, 3)];
                    if (i == selectedIndex)
                    {
                        EditorGUI.DrawRect(new Rect(x - 9, y - 9, 18, 18), new Color(1f, 1f, 0.2f, 0.35f));
                    }
                    // 小型点击画小一点（半径更小），普通 Tap 正常
                    float half = n.isSmallTap ? 4f : 6f;
                    EditorGUI.DrawRect(new Rect(x - half, y - half, half * 2, half * 2), c);
                }
            }

            // 链接模式跟随预览：最后已落节点 → 鼠标指针
            if (linkingMode && linkingActive && linkTimes.Count > 0)
            {
                Vector2 cur = Event.current.mousePosition;
                float xLast = timeX0 + (linkTimes[linkTimes.Count - 1] - viewStartTime) * pixelsPerSecond;
                float yLast = baseRect.y + RulerHeight + linkLanes[linkLanes.Count - 1] * LaneHeight + LaneHeight / 2;
                DrawThickLine(new Vector2(xLast, yLast), cur, 4f, new Color(0.3f, 0.9f, 1f, 0.7f));
            }

            // ---- 播放头 ----
            float phx = timeX0 + (playTime - viewStartTime) * pixelsPerSecond;
            EditorGUI.DrawRect(new Rect(phx, baseRect.y, 2, baseRect.height), Color.red);

            // 滚动条
            viewStartTime = GUILayout.HorizontalScrollbar(viewStartTime, visibleSeconds, 0f, songLength + 1f);

            HandleTimelineEvents(baseRect, timeX0);
        }

        private void HandleTimelineEvents(Rect baseRect, float timeX0)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseDrag &&
                e.type != EventType.MouseUp && e.type != EventType.KeyDown) return;
            if (e.type == EventType.KeyDown)
            {
                if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selectedIndex >= 0)
                {
                    notes.RemoveAt(selectedIndex);
                    selectedIndex = -1;
                    Repaint();
                    e.Use();
                }
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

            int lane = Mathf.Clamp(Mathf.FloorToInt((localY - RulerHeight) / LaneHeight), 0, 3);
            float time = (e.mousePosition.x - timeX0) / pixelsPerSecond + viewStartTime;
            time = Mathf.Clamp(time, 0f, songLength);

            if (e.type == EventType.MouseDown)
            {
                int hit = HitTestNote(e.mousePosition, baseRect, timeX0);

                if (e.button == 1) // 右键删除（普通/长按都适用）
                {
                    if (hit >= 0)
                    {
                        notes.RemoveAt(hit);
                        if (selectedIndex == hit) selectedIndex = -1;
                        Repaint();
                        e.Use();
                    }
                    return;
                }

                if (linkingMode)
                {
                    // 链接模式：左键落下一个节点；右键（在跟随阶段）结束整条链
                    if (e.button == 1)
                    {
                        FinishLinkingChain();
                        Repaint();
                        e.Use();
                        return;
                    }
                    // 左键：落节点
                    float snapped = snapToBeat ? Snap(time) : time;
                    if (!linkingActive)
                    {
                        // 第一个节点：创建一条尚在编辑中的多节点 Hold 本体（先只含 1 个节点）
                        var hn = new ChartNote
                        {
                            time = snapped,
                            lane = lane,
                            type = NoteData.NoteType.Hold,
                            holdLanes = new int[] { lane },
                            holdTimes = new float[] { snapped }
                        };
                        notes.Add(hn);
                        linkLastIndex = notes.Count - 1;
                        linkTimes.Clear(); linkLanes.Clear();
                        linkTimes.Add(snapped); linkLanes.Add(lane);
                        linkingActive = true;
                    }
                    else
                    {
                        // 后续节点：把当前编辑中的 Hold 追加一个节点（与上一节点自动链接）
                        linkTimes.Add(snapped);
                        linkLanes.Add(lane);
                        var hn = notes[linkLastIndex];
                        hn.holdLanes = linkLanes.ToArray();
                        hn.holdTimes = linkTimes.ToArray();
                        // 同步兼容旧字段：head=第1节点，tail=最后节点
                        hn.lane = linkLanes[0];
                        hn.holdEndLane = lane;
                        hn.holdDuration = Mathf.Max(0.1f, linkTimes[linkTimes.Count - 1] - linkTimes[0]);
                    }
                    selectedIndex = linkLastIndex;
                    Repaint();
                    e.Use();
                    return;
                }

                if (hit >= 0) // 选中并准备拖拽
                {
                    selectedIndex = hit;
                    dragIndex = hit;
                    dragNote = notes[hit];
                    dragStartMouse = e.mousePosition;
                    dragStartNoteTime = dragNote.time;
                    dragStartNoteLane = dragNote.lane;
                }
                else if (placeSmallTapMode) // 新增小型点击音符
                {
                    float snapped = snapToBeat ? Snap(time) : time;
                    var nn = new ChartNote { time = snapped, lane = lane, isSmallTap = true };
                    notes.Add(nn);
                    selectedIndex = notes.Count - 1;
                    dragIndex = selectedIndex;
                    dragNote = nn;
                    dragStartMouse = e.mousePosition;
                    dragStartNoteTime = nn.time;
                    dragStartNoteLane = nn.lane;
                }
                else // 新增普通音符
                {
                    float snapped = snapToBeat ? Snap(time) : time;
                    var nn = new ChartNote { time = snapped, lane = lane };
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
                    int nlane = Mathf.Clamp(Mathf.FloorToInt((e.mousePosition.y - baseRect.y - RulerHeight) / LaneHeight), 0, 3);

                    if (dragNote.type == NoteData.NoteType.Hold)
                    {
                        // 多节点 Hold 整体移动：head/tail 轨道差值保持，所有节点时间整体平移
                        int delta = nlane - dragNote.lane;
                        float dt = (snapToBeat ? Snap(nt) : nt) - dragNote.time;
                        dragNote.lane = Mathf.Clamp(nlane, 0, 3);
                        if (dragNote.holdLanes != null && dragNote.holdTimes != null && dragNote.holdLanes.Length >= 2)
                        {
                            for (int k = 0; k < dragNote.holdLanes.Length; k++)
                            {
                                dragNote.holdLanes[k] = Mathf.Clamp(dragNote.holdLanes[k] + delta, 0, 3);
                                dragNote.holdTimes[k] = Mathf.Max(0f, dragNote.holdTimes[k] + dt);
                            }
                            dragNote.holdEndLane = dragNote.holdLanes[dragNote.holdLanes.Length - 1];
                            dragNote.time = dragNote.holdTimes[0];
                            dragNote.holdDuration = Mathf.Max(0.1f, dragNote.holdTimes[dragNote.holdTimes.Length - 1] - dragNote.holdTimes[0]);
                        }
                        else
                        {
                            dragNote.holdEndLane = Mathf.Clamp(dragNote.holdEndLane + delta, 0, 3);
                            dragNote.time = snapToBeat ? Snap(nt) : nt;
                        }
                    }
                    else
                    {
                        dragNote.time = snapToBeat ? Snap(nt) : nt;
                        dragNote.lane = nlane;
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
                if (n.type == NoteData.NoteType.Hold)
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
                        float y = baseRect.y + RulerHeight + ls[k] * LaneHeight + LaneHeight / 2;
                        if (Near(mouse, x, y)) return i;
                    }
                }
                else
                {
                    float x = timeX0 + (n.time - viewStartTime) * pixelsPerSecond;
                    float y = baseRect.y + RulerHeight + n.lane * LaneHeight + LaneHeight / 2;
                    if (Near(mouse, x, y)) return i;
                }
            }
            return -1;
        }

        private static bool Near(Vector2 mouse, float x, float y)
        {
            return Mathf.Abs(mouse.x - x) <= 8f && Mathf.Abs(mouse.y - y) <= 18f;
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

        private float Snap(float time)
        {
            float secPerBeat = 60f / Mathf.Max(0.001f, bpm);
            return Mathf.Round(time / secPerBeat) * secPerBeat;
        }

        /// <summary>
        /// 结束正在编辑的链接链：
        /// - 已落 >=2 个节点 → 保留为一条多节点 Hold（holdLanes/holdTimes 已填好）。
        /// - 仅落 1 个节点 → 降级为普通点击音符（Tap）。
        /// 无论如何都清理链接编辑状态。
        /// </summary>
        private void FinishLinkingChain()
        {
            if (linkingActive && linkLastIndex >= 0 && linkLastIndex < notes.Count)
            {
                var hn = notes[linkLastIndex];
                if (linkTimes.Count <= 1)
                {
                    // 单节点：降级为普通 Tap
                    hn.type = NoteData.NoteType.Tap;
                    hn.holdLanes = null;
                    hn.holdTimes = null;
                    hn.holdDuration = 0f;
                    hn.holdEndLane = hn.lane;
                }
                else
                {
                    hn.holdLanes = linkLanes.ToArray();
                    hn.holdTimes = linkTimes.ToArray();
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
                bool active = (path == activePath);
                string name = Path.GetFileNameWithoutExtension(path);
                bool hasMusic = bm.audioClip != null;

                EditorGUILayout.BeginHorizontal(active ? EditorStyles.helpBox : GUIStyle.none);
                EditorGUILayout.LabelField($"{(active ? "★ " : "")}{name}  ({side0} 音符, BPM {bm.bpm:F0}){(hasMusic ? "  [音乐]" : "")}",
                    GUILayout.Width(320));
                if (GUILayout.Button("编辑", GUILayout.Width(50))) LoadBeatmap(path);
                if (GUILayout.Button("调用", GUILayout.Width(50))) SetActiveBeatmap(path);
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
            Debug.Log($"[谱面编辑器] 已调用谱面：{path}");
            ShowNotification(new GUIContent("已调用：" + Path.GetFileNameWithoutExtension(path)));
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
            notes = new List<ChartNote>();
            if (bm.notes != null)
            {
                foreach (var n in bm.notes)
                {
                    if (n.side == 0)
                    {
                        var cn = new ChartNote
                        {
                            time = n.time,
                            lane = n.lane,
                            type = n.type,
                            holdDuration = n.holdDuration,
                            holdEndLane = n.holdEndLane
                        };
                        if (n.type == NoteData.NoteType.SmallTap) cn.isSmallTap = true;
                        // 多节点 Hold 字段：若谱面存了则还原（编辑器当前只生成 2 节点，保持 null）
                        if (n.holdLanes != null) cn.holdLanes = (int[])n.holdLanes.Clone();
                        if (n.holdTimes != null) cn.holdTimes = (float[])n.holdTimes.Clone();
                        notes.Add(cn);
                    }
                }
            }
            if (notes.Count > 0) songLength = Mathf.Max(songLength, notes.Max(n => n.time) + 4f);
            selectedIndex = -1;
            dragIndex = -1;
            dragNote = null;
            linkingActive = false;
            linkLastIndex = -1;
            linkTimes.Clear();
            linkLanes.Clear();
            playTime = 0f;
            Repaint();
        }

        private void NewChart()
        {
            if (notes.Count > 0 &&
                !EditorUtility.DisplayDialog("新建谱面", "当前谱面尚未保存，确定新建空白谱面？", "新建", "取消"))
            {
                return;
            }
            notes = new List<ChartNote>();
            beatmapName = "NewBeatmap";
            currentEditingPath = "";
            beatmapAudioClip = null;
            selectedIndex = -1;
            dragIndex = -1;
            dragNote = null;
            linkingActive = false;
            linkLastIndex = -1;
            linkTimes.Clear();
            linkLanes.Clear();
            playTime = 0f;
            Repaint();
        }

        private void SaveBeatmap()
        {
            // 保存前若有未结束的链接链，先固化（避免半条链被保存）
            if (linkingActive) FinishLinkingChain();

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
            asset.bpm = bpm;
            asset.audioClip = beatmapAudioClip;
            asset.notes = BuildBothSideNotes();

            if (created) AssetDatabase.CreateAsset(asset, path);
            else AssetDatabase.SaveAssetIfDirty(asset);
            AssetDatabase.SaveAssets();
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
                var nd0 = new NoteData { time = n.time, lane = n.lane, side = 0 };
                var nd1 = new NoteData { time = n.time, lane = n.lane, side = 1 };

                if (n.isSmallTap)
                {
                    nd0.type = NoteData.NoteType.SmallTap;
                    nd1.type = NoteData.NoteType.SmallTap;
                }
                else if (n.type == NoteData.NoteType.Hold)
                {
                    nd0.type = NoteData.NoteType.Hold;
                    nd1.type = NoteData.NoteType.Hold;
                    // 多节点 Hold（优先）或旧式 2 节点 Hold
                    if (n.holdLanes != null && n.holdTimes != null && n.holdLanes.Length >= 2 && n.holdTimes.Length >= 2)
                    {
                        nd0.holdLanes = (int[])n.holdLanes.Clone();
                        nd1.holdLanes = (int[])n.holdLanes.Clone();
                        nd0.holdTimes = (float[])n.holdTimes.Clone();
                        nd1.holdTimes = (float[])n.holdTimes.Clone();
                    }
                    else
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
