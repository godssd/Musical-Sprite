#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace MusicalSprite.EditorTools
{
    /// <summary>
    /// 把 Krita / Aseprite 导出的 PNG 序列帧，一键拼成 Sprite Sheet，
    /// 自动设好导入参数并按等分网格切片，可选生成 Sprite Atlas。
    /// 菜单：Tools -> Musical-Sprite -> Sprite Sheet Builder
    /// </summary>
    public class SpriteSheetBuilderWindow : EditorWindow
    {
        private const string MenuPath = "Tools/Musical-Sprite/Sprite Sheet Builder";
        private const string DefaultOutput = "Assets/Art/VFX";

        private enum SourceMode
        {
            Folder = 0,
            Manual = 1
        }

        private enum SheetLayout
        {
            SingleRow = 0,
            Grid = 1
        }

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var w = GetWindow<SpriteSheetBuilderWindow>("Sprite Sheet Builder");
            w.minSize = new Vector2(430, 520);
            w.Show();
        }

        private SourceMode mode = SourceMode.Folder;
        private DefaultAsset sourceFolder;
        private readonly List<Texture2D> manualFrames = new List<Texture2D>();
        private Vector2 framesScroll;

        private string sheetName = "vfx_hit";
        private string outputFolder = DefaultOutput;
        private SheetLayout layout = SheetLayout.SingleRow;
        private int columns = 6;
        private int padding = 0;
        private int pixelsPerUnit = 100;
        private bool filterPoint;
        private bool createAtlas = true;
        private string atlasName = "VFXAtlas";
        private bool autoSlice = true;
        private bool pingResult = true;

        private string lastReport = string.Empty;
        private bool lastBuildOK;

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(outputFolder)) outputFolder = DefaultOutput;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("序列帧 -> Sprite Sheet", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Krita 用 File -> Render Animation 导出 PNG 序列（不要裁剪），\n" +
                "把整个文件夹拖进来即可自动拼图并切片。",
                MessageType.None);

            EditorGUILayout.Space(6);
            DrawSource();

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("输出设置", EditorStyles.boldLabel);
            sheetName = EditorGUILayout.TextField("Sheet 名称", sheetName);
            outputFolder = EditorGUILayout.TextField("输出目录", outputFolder);

            layout = (SheetLayout)EditorGUILayout.EnumPopup("排布", layout);
            if (layout == SheetLayout.Grid)
            {
                columns = Mathf.Max(1, EditorGUILayout.IntField("每行帧数", columns));
            }
            padding = Mathf.Clamp(EditorGUILayout.IntField("帧间距(px)", padding), 0, 16);
            pixelsPerUnit = Mathf.Max(1, EditorGUILayout.IntField("Pixels Per Unit", pixelsPerUnit));
            filterPoint = EditorGUILayout.Toggle("Point 过滤(像素风)", filterPoint);
            autoSlice = EditorGUILayout.Toggle("自动等分切片", autoSlice);
            createAtlas = EditorGUILayout.Toggle("生成 Sprite Atlas", createAtlas);
            if (createAtlas)
            {
                EditorGUI.indentLevel++;
                atlasName = EditorGUILayout.TextField("Atlas 名称", atlasName);
                EditorGUI.indentLevel--;
            }
            pingResult = EditorGUILayout.Toggle("完成后选中结果", pingResult);

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(!CanBuild()))
            {
                if (GUILayout.Button("生成 Sprite Sheet", GUILayout.Height(32)))
                {
                    Build();
                }
            }

            if (!string.IsNullOrEmpty(lastReport))
            {
                EditorGUILayout.Space(8);
                var style = new GUIStyle(EditorStyles.helpBox);
                style.richText = false;
                style.wordWrap = true;
                EditorGUILayout.LabelField(lastReport, style);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("复制报告"))
                {
                    EditorGUIUtility.systemCopyBuffer = lastReport;
                }
                if (lastBuildOK && GUILayout.Button("复制帧名列表"))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildFrameNameList();
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawSource()
        {
            EditorGUILayout.LabelField("① 选择序列帧", EditorStyles.boldLabel);
            mode = (SourceMode)GUILayout.Toolbar((int)mode, new[] { "文件夹", "手动指定" });

            if (mode == SourceMode.Folder)
            {
                var next = (DefaultAsset)EditorGUILayout.ObjectField("PNG 文件夹", sourceFolder, typeof(DefaultAsset), false);
                if (next != sourceFolder)
                {
                    sourceFolder = next;
                    if (sourceFolder != null)
                    {
                        var p = AssetDatabase.GetAssetPath(sourceFolder);
                        if (!AssetDatabase.IsValidFolder(p))
                        {
                            Debug.LogWarning("[SpriteSheetBuilder] 请选择文件夹，不是文件。");
                            sourceFolder = null;
                        }
                        else if (string.IsNullOrEmpty(sheetName) || sheetName == "vfx_hit")
                        {
                            sheetName = new DirectoryInfo(p).Name.ToLower();
                        }
                    }
                }
                if (sourceFolder != null)
                {
                    var files = CollectFromFolder();
                    EditorGUILayout.LabelField($"共 {files.Count} 张 PNG（按名称自然排序）", EditorStyles.miniLabel);
                    if (files.Count > 0)
                    {
                        framesScroll = EditorGUILayout.BeginScrollView(framesScroll, GUILayout.MaxHeight(110));
                        for (var i = 0; i < files.Count; i++)
                        {
                            EditorGUILayout.LabelField($"{i:00}  {Path.GetFileName(files[i])}", EditorStyles.miniLabel);
                        }
                        EditorGUILayout.EndScrollView();
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("把 PNG 拖到下方列表（顺序即帧序）", EditorStyles.miniLabel);
                var count = Mathf.Max(1, manualFrames.Count);
                while (manualFrames.Count < count) manualFrames.Add(null);
                for (var i = 0; i < manualFrames.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{i:00}", GUILayout.Width(26));
                    manualFrames[i] = (Texture2D)EditorGUILayout.ObjectField(manualFrames[i], typeof(Texture2D), false);
                    if (GUILayout.Button("-", GUILayout.Width(22)))
                    {
                        manualFrames.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                    EditorGUILayout.EndHorizontal();
                }
                if (GUILayout.Button("+ 增加一帧"))
                {
                    manualFrames.Add(null);
                }
            }
        }

        private bool CanBuild()
        {
            if (string.IsNullOrEmpty(sheetName)) return false;
            if (string.IsNullOrEmpty(outputFolder)) return false;
            if (mode == SourceMode.Folder) return sourceFolder != null && CollectFromFolder().Count > 0;
            return manualFrames.Exists(t => t != null);
        }

        private List<string> CollectFromFolder()
        {
            var result = new List<string>();
            if (sourceFolder == null) return result;
            var dir = AssetDatabase.GetAssetPath(sourceFolder);
            if (!AssetDatabase.IsValidFolder(dir)) return result;
            var abs = ToAbsolute(dir);
            if (!Directory.Exists(abs)) return result;
            var files = Directory.GetFiles(abs, "*.png", SearchOption.TopDirectoryOnly);
            foreach (var f in files)
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(f);
            }
            result.Sort(NaturalCompare);
            return result;
        }

        private List<string> ResolveSources()
        {
            if (mode == SourceMode.Folder) return CollectFromFolder();

            var list = new List<string>();
            foreach (var t in manualFrames)
            {
                if (t == null) continue;
                var p = AssetDatabase.GetAssetPath(t);
                if (string.IsNullOrEmpty(p)) continue;
                list.Add(ToAbsolute(p));
            }
            return list;
        }

        private void Build()
        {
            lastBuildOK = false;
            var sources = ResolveSources();
            var report = new StringBuilder();

            var frames = new List<Texture2D>();
            var names = new List<string>();
            try
            {
                foreach (var path in sources)
                {
                    var bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
                    if (!tex.LoadImage(bytes))
                    {
                        report.AppendLine($"解析失败：{Path.GetFileName(path)}");
                        continue;
                    }
                    tex.name = Path.GetFileNameWithoutExtension(path);
                    frames.Add(tex);
                    names.Add(tex.name);
                }

                if (frames.Count == 0)
                {
                    lastReport = "没有可用的 PNG。";
                    return;
                }

                var fw = frames[0].width;
                var fh = frames[0].height;
                var bad = new List<string>();
                for (var i = 0; i < frames.Count; i++)
                {
                    if (frames[i].width != fw || frames[i].height != fh)
                    {
                        bad.Add($"{names[i]} ({frames[i].width}x{frames[i].height})");
                    }
                }
                if (bad.Count > 0)
                {
                    EditorUtility.DisplayDialog("尺寸不一致",
                        "序列帧必须全部同尺寸，否则切片会错位：\n\n" +
                        string.Join("\n", bad.ToArray()) +
                        $"\n\n期望尺寸：{fw}x{fh}\n\n请在 Krita 里重新导出（不要勾选裁剪）。",
                        "知道了");
                    lastReport = "尺寸不一致，已中止。\n" + string.Join("\n", bad.ToArray());
                    return;
                }

                var cols = layout == SheetLayout.SingleRow ? frames.Count : Mathf.Max(1, columns);
                cols = Mathf.Clamp(cols, 1, frames.Count);
                var rows = Mathf.CeilToInt((float)frames.Count / cols);

                var cellW = fw + padding * 2;
                var cellH = fh + padding * 2;
                var sheetW = cellW * cols;
                var sheetH = cellH * rows;

                var sheet = new Texture2D(sheetW, sheetH, TextureFormat.RGBA32, false, false);
                var clear = new Color32[sheetW * sheetH];
                sheet.SetPixels32(clear, 0);

                for (var i = 0; i < frames.Count; i++)
                {
                    var col = i % cols;
                    var rowFromTop = i / cols;
                    var px = col * cellW + padding;
                    var pyFromBottom = sheetH - (rowFromTop + 1) * cellH + padding;
                    sheet.SetPixels32(px, pyFromBottom, fw, fh, frames[i].GetPixels32(0), 0);
                }
                sheet.Apply(false, false);

                if (!Directory.Exists(ToAbsolute(outputFolder)))
                {
                    Directory.CreateDirectory(ToAbsolute(outputFolder));
                }
                var outPath = $"{outputFolder.TrimEnd('/')}/{sheetName}.png";
                File.WriteAllBytes(ToAbsolute(outPath), sheet.EncodeToPNG());
                AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);

                var importer = (TextureImporter)AssetImporter.GetAtPath(outPath);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = autoSlice ? SpriteImportMode.Multiple : SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = filterPoint ? FilterMode.Point : FilterMode.Bilinear;
                importer.spritePixelsPerUnit = pixelsPerUnit;
                importer.spritePivot = new Vector2(0.5f, 0.5f);
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = Mathf.Max(2048, Mathf.NextPowerOfTwo(Mathf.Max(sheetW, sheetH)));

                if (autoSlice)
                {
                    var meta = new SpriteMetaData[frames.Count];
                    for (var i = 0; i < frames.Count; i++)
                    {
                        var col = i % cols;
                        var rowFromTop = i / cols;
                        var x = col * cellW + padding;
                        var yFromBottom = sheetH - (rowFromTop + 1) * cellH + padding;
                        meta[i] = new SpriteMetaData
                        {
                            name = $"{sheetName}_{i:00}",
                            rect = new Rect(x, yFromBottom, fw, fh),
                            alignment = (int)SpriteAlignment.Center,
                            pivot = new Vector2(0.5f, 0.5f),
                            border = Vector4.zero
                        };
                    }
                    importer.spritesheet = meta;
                }

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);

                report.AppendLine($"Sheet：{outPath}");
                report.AppendLine($"尺寸：{sheetW} x {sheetH}（{cols} 列 x {rows} 行，单帧 {fw}x{fh}）");
                report.AppendLine($"帧数：{frames.Count}   PPU：{pixelsPerUnit}");
                report.AppendLine($"世界尺寸：{(float)fw / pixelsPerUnit:0.##} x {(float)fh / pixelsPerUnit:0.##} 单位");
                report.AppendLine($"切片：{(autoSlice ? $"{sheetName}_00 ~ {sheetName}_{frames.Count - 1:00}" : "未切片")}");

                if (createAtlas)
                {
                    var atlasPath = $"{outputFolder.TrimEnd('/')}/{atlasName}.spriteatlas";
                    SpriteAtlas atlas;
                    if (File.Exists(ToAbsolute(atlasPath)))
                    {
                        atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
                    }
                    else
                    {
                        atlas = new SpriteAtlas();
                        AssetDatabase.CreateAsset(atlas, atlasPath);
                        var texSettings = new SpriteAtlasTextureSettings
                        {
                            readable = false,
                            generateMipMaps = false,
                            sRGB = true,
                            filterMode = filterPoint ? FilterMode.Point : FilterMode.Bilinear
                        };
                        SpriteAtlasExtensions.SetTextureSettings(atlas, texSettings);
                        var packSettings = new SpriteAtlasPackingSettings
                        {
                            blockOffset = 1,
                            enableRotation = false,
                            enableTightPacking = false,
                            padding = 2
                        };
                        SpriteAtlasExtensions.SetPackingSettings(atlas, packSettings);
                        atlas.SetIncludeInBuild(true);
                    }

                    if (atlas != null)
                    {
                        var folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(outputFolder);
                        if (folderAsset != null)
                        {
                            SpriteAtlasExtensions.Add(atlas, new[] { folderAsset });
                        }
                        else
                        {
                            var sprites = new List<UnityEngine.Object>();
                            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(outPath))
                            {
                                if (o is Sprite) sprites.Add(o);
                            }
                            SpriteAtlasExtensions.Add(atlas, sprites.ToArray());
                        }
                        EditorUtility.SetDirty(atlas);
                        AssetDatabase.SaveAssets();
                        report.AppendLine($"Atlas：{atlasPath}");
                    }
                }

                lastBuildOK = true;
                Debug.Log($"[SpriteSheetBuilder] 完成：{outPath}\n{report}");
                if (pingResult)
                {
                    Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(outPath);
                    EditorGUIUtility.PingObject(Selection.activeObject);
                }
            }
            catch (Exception e)
            {
                report.AppendLine("异常：" + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                foreach (var t in frames)
                {
                    if (t != null) DestroyImmediate(t);
                }
            }

            lastReport = report.ToString();
            Repaint();
        }

        private string BuildFrameNameList()
        {
            var count = mode == SourceMode.Folder ? CollectFromFolder().Count : manualFrames.FindAll(t => t != null).Count;
            var sb = new StringBuilder();
            for (var i = 0; i < count; i++) sb.AppendLine($"{sheetName}_{i:00}");
            return sb.ToString();
        }

        private static string ToAbsolute(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static int NaturalCompare(string a, string b)
        {
            var ia = 0;
            var ib = 0;
            var cmp = CompareInfo.GetCompareInfo(CultureInfo.InvariantCulture.Name);
            while (ia < a.Length && ib < b.Length)
            {
                if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
                {
                    long na = 0;
                    long nb = 0;
                    while (ia < a.Length && char.IsDigit(a[ia])) na = na * 10 + (a[ia++] - '0');
                    while (ib < b.Length && char.IsDigit(b[ib])) nb = nb * 10 + (b[ib++] - '0');
                    if (na != nb) return na < nb ? -1 : 1;
                }
                else
                {
                    var r = cmp.Compare(a[ia].ToString(), b[ib].ToString(), CompareOptions.IgnoreCase);
                    if (r != 0) return r;
                    ia++;
                    ib++;
                }
            }
            return a.Length.CompareTo(b.Length);
        }
    }
}
#endif
