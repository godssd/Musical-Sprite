#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 一键把场景里的旧血条（HPBarDisplay）升级成液体血条（HPBarLiquid）。
///
/// 使用方式：Unity 菜单 → Tools → Musical-Sprite → Setup Liquid HP Bars
///
/// 会做的事：
/// 1. 把 Art/UI 下的 PNG 导入设置改成 Sprite 并关闭压缩，保证手绘边缘干净
/// 2. 为每条血条补齐 Background / Fill / Frame 三个子节点并指定素材
/// 3. 给 Fill 挂上液体 Shader 的材质球
/// 4. 移除旧的 HPBarDisplay，挂上 HPBarLiquid 并接好引用
/// 5. 保存场景
///
/// 脚本可重复运行：
///   - 首次运行  → 升级 HPBarDisplay
///   - 再次运行  → 刷新已有 HPBarLiquid 的素材与材质
/// </summary>
public static class HPBarLiquidSetup
{
    private const string ContainerPath  = "Assets/Art/UI/HPBar_Container.png";
    private const string FramePath      = "Assets/Art/UI/HPBar_Frame.png";
    private const string DecorationPath = "Assets/Art/UI/HPBar_decoration.png";
    private const string ShaderName     = "MusicalSprite/UI/HPBarLiquid";
    private const string MaterialDir    = "Assets/Art/UI";
    private const string NumbersDir     = "Assets/Art/UI/Numbers";

    // 血条高度不再写死，而是根据 HPBar_Container 的宽高比自动计算，
    // 保证 Image.Type.Simple 不会把素材拉扁。当前素材约 5.8:1。

    [MenuItem("Tools/Musical-Sprite/Setup Liquid HP Bars")]
    public static void Setup()
    {
        // 1. 素材导入设置
        Sprite containerSprite  = EnsureSpriteImported(ContainerPath);
        Sprite frameSprite      = EnsureSpriteImported(FramePath);
        Sprite decorationSprite = EnsureSpriteImported(DecorationPath);

        if (containerSprite == null || frameSprite == null)
        {
            Debug.LogError($"[HPBarLiquidSetup] 找不到素材，请确认文件存在：\n{ContainerPath}\n{FramePath}");
            return;
        }

        if (decorationSprite == null)
        {
            Debug.LogWarning($"[HPBarLiquidSetup] 找不到装饰底图 {DecorationPath}，将跳过 Decoration 层。");
        }

        // 2. Shader
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"[HPBarLiquidSetup] 找不到 Shader '{ShaderName}'，请确认 Assets/Shaders/HPBarLiquid.shader 存在。");
            return;
        }

        int count = 0;

        // 3a. 先升级还没升级的旧血条
        HPBarDisplay[] displays = Object.FindObjectsByType<HPBarDisplay>(FindObjectsSortMode.None);
        foreach (HPBarDisplay display in displays)
        {
            if (UpgradeBar(display, containerSprite, frameSprite, decorationSprite, shader))
                count++;
        }

        // 3b. 如果已经升级过，就刷新素材与材质（方便换图后重跑）
        if (count == 0)
        {
            HPBarLiquid[] liquids = Object.FindObjectsByType<HPBarLiquid>(FindObjectsSortMode.None);
            foreach (HPBarLiquid liquid in liquids)
            {
                if (RefreshBar(liquid, containerSprite, frameSprite, decorationSprite, shader))
                    count++;
            }

            if (count == 0)
            {
                Debug.LogWarning("[HPBarLiquidSetup] 场景里没有找到 HPBarDisplay 或 HPBarLiquid 组件。");
                return;
            }
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log($"[HPBarLiquidSetup] 完成，共处理 {count} 条血条。");
        RunSelfCheck();
    }

    /// <summary>
    /// 自检清单（回归保护）：把"是否生效"变成可查的 PASS/FAIL，不靠肉眼对比旧图。
    /// 也可通过菜单 Tools → Musical-Sprite → Check HP Bars 单独运行。
    /// </summary>
    [MenuItem("Tools/Musical-Sprite/Check HP Bars")]
    public static void RunSelfCheck()
    {
        HPBarLiquid[] liquids = Object.FindObjectsByType<HPBarLiquid>(FindObjectsSortMode.None);
        if (liquids.Length == 0)
        {
            Debug.LogWarning("[HPBarLiquidSetup][自检] 场景里没有 HPBarLiquid，跳过。");
            return;
        }

        Debug.Log("════════ HP Bar 自检报告 ════════");
        bool allPass = true;

        foreach (HPBarLiquid bar in liquids)
        {
            Transform root = bar.transform;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append($"[{bar.name}]  ");

            bool pass = true;

            // 1. 旧 Border 残留（方形黑底来源）
            bool hasBorder = root.Find("Border") != null;
            pass &= !hasBorder;
            sb.Append(hasBorder ? "❌Border残留 " : "✅无Border ");

            // 2. 必需节点齐全
            bool hasGhost = root.Find("Ghost") != null;
            bool hasFlash = root.Find("FlashOverlay") != null;
            bool hasFill = root.Find("Fill") != null;
            bool hasFrame = root.Find("Frame") != null;
            bool hasBg = root.Find("Background") != null;
            pass &= hasGhost && hasFlash && hasFill && hasFrame && hasBg;
            sb.Append(hasGhost ? "✅Ghost " : "❌Ghost缺 ");
            sb.Append(hasFlash ? "✅Flash " : "❌Flash缺 ");
            sb.Append(hasFill ? "✅Fill " : "❌Fill缺 ");
            sb.Append(hasFrame ? "✅Frame " : "❌Frame缺 ");

            // 3. 素材已转 Sprite（Fill 有 sprite 且不是 Default 类型）
            Image fillImg = bar.fillImage != null ? bar.fillImage : root.Find("Fill")?.GetComponent<Image>();
            bool fillSpriteOK = fillImg != null && fillImg.sprite != null && fillImg.sprite.texture != null;
            pass &= fillSpriteOK;
            sb.Append(fillSpriteOK ? "✅Fill贴图 " : "❌Fill贴图缺 ");

            // 4. 数字组件在（图片或 Text 至少一个）
            bool hasNumber = bar.GetComponentInChildren<HPNumberSprite>() != null ||
                             bar.GetComponentInChildren<Text>() != null;
            pass &= hasNumber;
            sb.Append(hasNumber ? "✅数字 " : "❌数字缺 ");

            // 5. Ghost / Flash 引用已接好
            bool refsOK = bar.ghostImage != null && bar.flashImage != null;
            pass &= refsOK;
            sb.Append(refsOK ? "✅引用 " : "❌引用缺 ");

            allPass &= pass;
            Debug.Log(sb.ToString() + (pass ? "→ PASS" : "→ FAIL"));
        }

        Debug.Log(allPass
            ? "════════ 自检全部 PASS ✅ ════════"
            : "════════ 自检存在 FAIL ❌，请根据上述明细修复 ════════");
    }

    /// <summary>把旧组件换成新组件。</summary>
    private static bool UpgradeBar(HPBarDisplay display, Sprite containerSprite, Sprite frameSprite,
                                   Sprite decorationSprite, Shader shader)
    {
        GameObject root = display.gameObject;

        int side = display.side;
        ScoreManager scoreManager = display.scoreManager;
        Text hpText = display.hpText;

        Image fillImg = BuildHierarchy(root.transform, containerSprite, frameSprite, decorationSprite,
                                       shader, display.fillImage, out Text resolvedText);

        if (hpText == null)
            hpText = resolvedText;

        Object.DestroyImmediate(display);

        HPBarLiquid liquid = root.GetComponent<HPBarLiquid>();
        if (liquid == null)
            liquid = root.AddComponent<HPBarLiquid>();

        liquid.side = side;
        liquid.scoreManager = scoreManager;
        liquid.fillImage = fillImg;
        liquid.hpText = hpText;
        liquid.ghostImage = root.transform.Find("Ghost")?.GetComponent<Image>();
        liquid.flashImage = root.transform.Find("FlashOverlay")?.GetComponent<Image>();

        ApplySideColors(liquid);
        liquid.hpNumberSprite = TrySetupHPNumberSprite(root.transform);
        SnapToMaterialAspect(root, containerSprite);

        EditorUtility.SetDirty(liquid);
        EditorUtility.SetDirty(root);

        Debug.Log($"[HPBarLiquidSetup] 已升级 '{root.name}'（side={side}）");
        return true;
    }

    /// <summary>已经升级过时，只刷新素材与材质。</summary>
    private static bool RefreshBar(HPBarLiquid liquid, Sprite containerSprite, Sprite frameSprite,
                                   Sprite decorationSprite, Shader shader)
    {
        GameObject root = liquid.gameObject;

        Image fillImg = BuildHierarchy(root.transform, containerSprite, frameSprite, decorationSprite,
                                       shader, liquid.fillImage, out Text resolvedText);

        liquid.fillImage = fillImg;
        if (liquid.hpText == null)
            liquid.hpText = resolvedText;
        liquid.ghostImage = root.transform.Find("Ghost")?.GetComponent<Image>();
        liquid.flashImage = root.transform.Find("FlashOverlay")?.GetComponent<Image>();

        ApplySideColors(liquid);
        liquid.hpNumberSprite = TrySetupHPNumberSprite(root.transform);
        SnapToMaterialAspect(root, containerSprite);

        EditorUtility.SetDirty(liquid);
        EditorUtility.SetDirty(root);

        Debug.Log($"[HPBarLiquidSetup] 已刷新 '{root.name}'（side={liquid.side}）");
        return true;
    }

    /// <summary>
    /// 让血条 RectTransform 的宽高比与素材保持一致。
    /// 只改高度、保留宽度，避免破坏现有横向布局。
    /// </summary>
    private static void SnapToMaterialAspect(GameObject root, Sprite containerSprite)
    {
        RectTransform rt = root.GetComponent<RectTransform>();
        if (rt == null || containerSprite == null || containerSprite.texture == null) return;

        float spriteAspect = (float)containerSprite.texture.width / containerSprite.texture.height;
        float targetHeight = rt.sizeDelta.x / spriteAspect;

        if (Mathf.Abs(rt.sizeDelta.y - targetHeight) > 0.5f)
        {
            Debug.LogWarning(
                $"[HPBarLiquidSetup] '{root.name}' 高度 {rt.sizeDelta.y} 与素材比例 {spriteAspect:F2}:1 不匹配，" +
                $"已自动调整为 {rt.sizeDelta.x}×{targetHeight:F0}。若不满意请在 Inspector 里手动改。");
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, targetHeight);
        }
    }

    /// <summary>
    /// 根据 side 设置红/蓝两套颜色，并同步到材质球，让编辑器预览也正确。
    /// </summary>
    private static void ApplySideColors(HPBarLiquid liquid)
    {
        if (liquid.side == 0)
        {
            // 红方：改成纯色（上下一致，不渐变、不发灰）
            liquid.topColor = new Color(1.00f, 0.15f, 0.00f);
            liquid.bottomColor = new Color(1.00f, 0.15f, 0.00f);
            liquid.crestColor = new Color(1.00f, 0.35f, 0.25f);
            liquid.surfaceDarken = new Color(1.00f, 0.15f, 0.00f);
        }
        else
        {
            // 蓝方
            liquid.topColor = new Color(0.05f, 0.28f, 1.00f);
            liquid.bottomColor = new Color(0.05f, 0.28f, 1.00f);
            liquid.crestColor = new Color(0.45f, 0.75f, 1.00f);
            liquid.surfaceDarken = new Color(0.05f, 0.28f, 1.00f);
        }
        liquid.surfaceDarkenRange = 0.0f;
        liquid.edgeSoftness = 0.004f;
        liquid.crestWidth = 0.08f;
        liquid.crestIntensity = 1.0f;
        liquid.surfaceGlow = 0.4f;

        string matPath = $"{MaterialDir}/M_HPBarLiquid_{liquid.gameObject.name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null)
        {
            mat.SetColor("_TopColor", liquid.topColor);
            mat.SetColor("_BottomColor", liquid.bottomColor);
            mat.SetColor("_CrestColor", liquid.crestColor);
            mat.SetColor("_SurfaceDarken", liquid.surfaceDarken);
            mat.SetFloat("_SurfaceDarkenRange", liquid.surfaceDarkenRange);
            mat.SetFloat("_SloshFreq", 2.2f);
            mat.SetFloat("_SloshSpeed", 7f);
            mat.SetFloat("_WaveFreq", liquid.waveFreq);
            mat.SetFloat("_WaveSpeed", liquid.waveSpeed);
            mat.SetFloat("_RippleScale", liquid.rippleScale);
            mat.SetFloat("_AmbientWave", liquid.ambientWave);
            mat.SetFloat("_EdgeSoftness", liquid.edgeSoftness);
            mat.SetFloat("_CrestWidth", liquid.crestWidth);
            mat.SetFloat("_CrestIntensity", liquid.crestIntensity);
            mat.SetFloat("_SurfaceGlow", liquid.surfaceGlow);
            // 纯色模式：关闭顶部白发反光和血球纹理
            mat.SetColor("_TopGlossColor", new Color(1.00f, 0.15f, 0.00f, 1f));
            mat.SetFloat("_TopGlossIntensity", 0f);
            mat.SetFloat("_BlobIntensity", 0f);
            // 血球参数仍写进去，方便以后想开的时候在 Inspector 调
            mat.SetFloat("_BlobFreq", 5.5f);
            mat.SetFloat("_BlobThreshold", 0.42f);
            mat.SetFloat("_BlobSoftness", 0.22f);
            EditorUtility.SetDirty(mat);
        }

        // 同步 Ghost（黄色残影）材质：镜像方向与阵营一致，并关掉一切会破坏"平涂"的参数
        string gpath = $"{MaterialDir}/M_HPBarLiquid_{liquid.gameObject.name}_Ghost.mat";
        Material gmat = AssetDatabase.LoadAssetAtPath<Material>(gpath);
        if (gmat != null)
        {
            gmat.SetFloat("_Flip", liquid.side == 0 ? 0f : 1f);
            gmat.SetFloat("_AmbientWave", 0f);
            gmat.SetFloat("_RippleScale", 0f);
            gmat.SetFloat("_CrestIntensity", 0f);
            gmat.SetFloat("_SurfaceGlow", 0f);
            gmat.SetFloat("_TopGlossIntensity", 0f);
            gmat.SetFloat("_BlobIntensity", 0f);
            gmat.SetFloat("_SurfaceDarkenRange", 0f);
            EditorUtility.SetDirty(gmat);
        }
    }

    /// <summary>
    /// 创建/获取某条血条的 Ghost（黄色残影）材质，并固定为平涂黄（无波纹、无渐变、无高光）。
    /// </summary>
    private static Material EnsureGhostMaterial(string rootName, Shader shader, Color ghostColor)
    {
        string path = $"{MaterialDir}/M_HPBarLiquid_{rootName}_Ghost.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        mat.SetColor("_TopColor", ghostColor);
        mat.SetColor("_BottomColor", ghostColor);
        mat.SetColor("_SurfaceDarken", ghostColor);
        mat.SetFloat("_SurfaceDarkenRange", 0f);
        mat.SetFloat("_CrestIntensity", 0f);
        mat.SetFloat("_SurfaceGlow", 0f);
        mat.SetFloat("_TopGlossIntensity", 0f);
        mat.SetFloat("_BlobIntensity", 0f);
        mat.SetFloat("_AmbientWave", 0f);
        mat.SetFloat("_RippleScale", 0f);
        mat.SetFloat("_Fill", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// 如果 Assets/Art/UI/Numbers/ 下放齐了 HP 字样和 0~9 数字，
    /// 就挂上 HPNumberSprite 用手绘图片拼数字，并隐藏原来的 Text。
    /// 素材不全则返回 null，血条继续用 Text。
    ///
    /// 命名优先级：
    ///   - 优先读 fight_lift_HP.png / fight_lift_0.png ~ fight_lift_9.png（你这次加的前缀）
    ///   - 找不到时回退到 HP.png / 0.png ~ 9.png（旧约定）
    /// </summary>
    private static HPNumberSprite TrySetupHPNumberSprite(Transform rootT)
    {
        // 注意：必须先走 EnsureSpriteImported 把 PNG 转成 Sprite 类型，
        // 否则 LoadAssetAtPath<Sprite> 会拿到 null（新建的图默认是 textureType:0 Default）。
        Sprite hpLabel = EnsureSpriteImported($"{NumbersDir}/fight_lift_HP.png");
        if (hpLabel == null)
            hpLabel = EnsureSpriteImported($"{NumbersDir}/HP.png");

        if (hpLabel == null)
        {
            Debug.LogWarning($"[HPBarLiquidSetup] 找不到 HP 字样图片（{NumbersDir}/fight_lift_HP.png 或 HP.png），继续用 Text。");
            return null;
        }

        var digits = new Sprite[10];
        for (int i = 0; i < 10; i++)
        {
            digits[i] = EnsureSpriteImported($"{NumbersDir}/fight_lift_{i}.png");
            if (digits[i] == null)
                digits[i] = EnsureSpriteImported($"{NumbersDir}/{i}.png");

            if (digits[i] == null)
            {
                Debug.LogWarning($"[HPBarLiquidSetup] 缺少数字 {i} 的图片（fight_lift_{i}.png 或 {i}.png），图片数字未启用，继续用 Text。");
                return null;
            }
        }

        Transform host = rootT.Find("HPNumber");
        if (host == null)
        {
            var go = new GameObject("HPNumber", typeof(RectTransform));
            go.transform.SetParent(rootT, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            host = go.transform;
        }

        var comp = host.GetComponent<HPNumberSprite>();
        if (comp == null)
            comp = host.gameObject.AddComponent<HPNumberSprite>();

        comp.hpLabel = hpLabel;
        comp.digits = digits;
        EditorUtility.SetDirty(comp);

        // 图片数字启用后隐藏 Text，避免和图片重影
        var txt = rootT.GetComponentInChildren<Text>();
        if (txt != null)
        {
            txt.gameObject.SetActive(false);
            EditorUtility.SetDirty(txt.gameObject);
        }

        host.SetAsLastSibling();

        Debug.Log($"[HPBarLiquidSetup] '{rootT.name}' 已启用手绘图片数字（{NumbersDir}/）。");
        return comp;
    }

    /// <summary>
    /// 补齐并配置 Decoration / Background / Fill / Frame / Text 子节点。
    /// 层级（从底到顶）：Decoration（深红底边） < Background（空槽黑底） < Fill（液体） < Frame（白描边） < Text/HPNumber。
    /// </summary>
    private static Image BuildHierarchy(Transform rootT, Sprite containerSprite, Sprite frameSprite,
                                        Sprite decorationSprite, Shader shader, Image existingFill, out Text outText)
    {
        outText = null;

        // 清理旧版 HPBarDisplay 残留的 Border 子节点，它就是“方形黑底”的来源
        Transform oldBorder = rootT.Find("Border");
        if (oldBorder != null)
        {
            Debug.LogWarning($"[HPBarLiquidSetup] '{rootT.name}' 发现旧版 Border 子节点，已删除以避免方形黑底。");
            Object.DestroyImmediate(oldBorder.gameObject);
        }

        // 比例自检：素材比例和血条 RectTransform 差太多时，图形会被拉伸变形
        CheckAspect(containerSprite, rootT as RectTransform, rootT.name);

        // --- Decoration：深红色底边装饰（放在最底层） ---
        Image decoImg = null;
        if (decorationSprite != null)
        {
            decoImg = EnsureChildImage(rootT, "Decoration", decorationSprite, Color.white);
        }

        // --- Background：空槽黑底（参考图空槽是纯黑） ---
        Image bgImg = EnsureChildImage(rootT, "Background", containerSprite, Color.black);

        // --- Ghost：黄色残影层（受击时显示被扣除的部分），位于 Background 之上、Fill 之下 ---
        Image ghostImg = EnsureChildImage(rootT, "Ghost", containerSprite, Color.white);
        Material ghostMat = EnsureGhostMaterial(rootT.name, shader, new Color(0.937f, 0.624f, 0.153f));
        ghostImg.material = ghostMat;

        // --- Fill：液体层（Shader 控制液面） ---
        Image fillImg = existingFill;
        if (fillImg == null)
        {
            fillImg = EnsureChildImage(rootT, "Fill", containerSprite, Color.white);
        }
        else
        {
            fillImg.sprite = containerSprite;
            fillImg.color = Color.white;
            fillImg.type = Image.Type.Simple;
            fillImg.raycastTarget = false;
            StretchRect(fillImg.rectTransform);
        }

        // 液体材质
        string matPath = $"{MaterialDir}/M_HPBarLiquid_{rootT.name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        fillImg.material = mat;
        EditorUtility.SetDirty(mat);

        // --- FlashOverlay：闪白层，位于 Fill 之上、Frame 之下（默认透明） ---
        // 用纯色矩形（sprite 传 null），靠 alpha 控制透明度。
        // 不能用黑底容器图：白 tint × 黑底 = 黑，会表现成"闪黑"而非"闪白"。
        Image flashImg = EnsureChildImage(rootT, "FlashOverlay", null, Color.white);
        var fc = flashImg.color; fc.a = 0f; flashImg.color = fc;

        // --- Frame：手绘白色描边 ---
        Image frameImg = EnsureChildImage(rootT, "Frame", frameSprite, Color.white);

        // 强制层级顺序（从底到顶）：
        // Decoration < Background < Ghost < Fill < FlashOverlay < Frame
        int baseIndex = decoImg != null ? 1 : 0;
        if (decoImg != null)
            decoImg.transform.SetSiblingIndex(0);
        bgImg.transform.SetSiblingIndex(baseIndex + 0);
        ghostImg.transform.SetSiblingIndex(baseIndex + 1);
        fillImg.transform.SetSiblingIndex(baseIndex + 2);
        flashImg.transform.SetSiblingIndex(baseIndex + 3);
        frameImg.transform.SetSiblingIndex(baseIndex + 4);

        // --- HP 文本（放在最上层） ---
        Transform t = rootT.Find("Text");
        if (t != null) outText = t.GetComponent<Text>();

        if (outText == null)
        {
            outText = rootT.GetComponentInChildren<Text>();
        }

        if (outText == null)
        {
            outText = CreateHPText(rootT);
        }

        if (outText != null)
        {
            StyleHPText(outText);
            outText.transform.SetAsLastSibling();
        }

        return fillImg;
    }

    /// <summary>
    /// 检查素材比例与血条 RectTransform 是否匹配。
    /// Image.Type.Simple 会把贴图拉伸填满矩形，比例不一致时图形会变形、描边被压扁。
    /// </summary>
    private static void CheckAspect(Sprite sprite, RectTransform barRect, string barName)
    {
        if (sprite == null || sprite.texture == null || barRect == null) return;

        float spriteAspect = (float)sprite.texture.width / sprite.texture.height;
        Rect r = barRect.rect;
        if (r.height <= 0f) return;

        float barAspect = r.width / r.height;
        float ratio = barAspect / spriteAspect;

        if (ratio > 1.2f || ratio < 0.83f)
        {
            int properHeight = Mathf.RoundToInt(sprite.texture.width / barAspect);
            Debug.LogWarning(
                $"[HPBarLiquidSetup] '{barName}' 素材比例不匹配：\n" +
                $"  素材 {sprite.texture.width}×{sprite.texture.height}（{spriteAspect:F2}:1）\n" +
                $"  血条 {(int)r.width}×{(int)r.height}（{barAspect:F2}:1）\n" +
                $"  图形会被拉伸 {ratio:F2} 倍 → 形状变形、描边被压扁。\n" +
                $"  建议：把素材改按 {barAspect:F2}:1 出图，宽度 {sprite.texture.width} 时高度应为 {properHeight}（即 {sprite.texture.width}×{properHeight}），且图形撑满画布不要留白。");
        }
    }

    private static Image EnsureChildImage(Transform root, string name, Sprite sprite, Color color)
    {
        Transform t = root.Find(name);
        GameObject go;

        if (t == null)
        {
            go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);
        }
        else
        {
            go = t.gameObject;
        }

        Image img = go.GetComponent<Image>();
        if (img == null)
            img = go.AddComponent<Image>();

        img.sprite = sprite;
        img.color = color;
        img.type = Image.Type.Simple;
        img.raycastTarget = false;

        StretchRect(go.GetComponent<RectTransform>());

        return img;
    }

    private static void StretchRect(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
    }

    private static Text CreateHPText(Transform root)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(root, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(14f, 0f);
        rt.offsetMax = new Vector2(-14f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Text txt = go.GetComponent<Text>();
        txt.font = GetBuiltinFont();
        txt.fontSize = 22;
        txt.fontStyle = FontStyle.Bold;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.raycastTarget = false;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;
        txt.text = "HP 0";

        return txt;
    }

    /// <summary>
    /// 给 HP 文本套上样式：加粗加大 + 深色描边阴影，尽量靠近参考图的手绘立体感。
    /// 工程里如果有自定义字体，在 Inspector 里直接替换 Font 即可。
    /// </summary>
    private static void StyleHPText(Text txt)
    {
        if (txt == null) return;

        if (txt.font == null)
            txt.font = GetBuiltinFont();

        txt.fontSize = 26;
        txt.fontStyle = FontStyle.Bold;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleLeft;
        txt.horizontalOverflow = HorizontalWrapMode.Overflow;
        txt.verticalOverflow = VerticalWrapMode.Overflow;

        // 加一层深色阴影，模拟参考图里数字的立体描边
        Shadow shadow = txt.GetComponent<Shadow>();
        if (shadow == null)
            shadow = txt.gameObject.AddComponent<Shadow>();

        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(2f, -2f);
        shadow.useGraphicAlpha = true;

        EditorUtility.SetDirty(txt);
    }

    /// <summary>
    /// 获取一个可用的内置字体（不同 Unity 版本的内置字体名不一样，逐个尝试）。
    /// 都拿不到时返回 null，用户可在 Inspector 里手动指定字体。
    /// </summary>
    private static Font GetBuiltinFont()
    {
        string[] candidates = { "LegacyRuntime.ttf", "Arial.ttf" };

        foreach (string name in candidates)
        {
            Font f = Resources.GetBuiltinResource<Font>(name);
            if (f != null)
                return f;
        }

        Debug.LogWarning("[HPBarLiquidSetup] 拿不到内置字体，请在 Inspector 里手动给 HP Text 指定字体。");
        return null;
    }

    /// <summary>
    /// 确保 PNG 以 Sprite 类型导入，并关闭压缩、关闭 mipmap，保证手绘边缘干净。
    /// </summary>
    private static Sprite EnsureSpriteImported(string assetPath)
    {
        TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        if (importer == null)
            return null;

        bool dirty = false;

        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            dirty = true;
        }

        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            dirty = true;
        }

        if (importer.textureCompression != TextureImporterCompression.Uncompressed)
        {
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            dirty = true;
        }

        if (dirty)
        {
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
    }
}
#endif
