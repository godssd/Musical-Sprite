using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 通用贴地 Blob 投影组件。挂在任何会站在地面的单位（角色、敌人、道具）上，
/// 运行时/编辑器预览都会创建一个朝上的透明圆斑 quad，自动跟随单位底部贴地，
/// 并随离地高度缩放/淡出；同时沿主光源反方向偏移并压扁，模拟 COTL 式卡通阴影。
/// 与具体单位模型解耦：美术后续替换角色模型时，只要保留此组件即可复用同一阴影逻辑。
/// </summary>
[ExecuteInEditMode]
public class BlobShadow : MonoBehaviour
{
    [Tooltip("Ground 层掩码，决定射线能命中哪些物体作为地面")]
    public LayerMask groundMask = ~0;

    [Tooltip("阴影贴地 quad 使用的材质（留空则从 Resources/Materials/M_BlobShadow 加载）")]
    public Material shadowMaterial;

    [Header("形状")]
    [Tooltip("单位站在地面时阴影的基准半径")]
    public float baseRadius = 0.35f;

    [Tooltip("离地越高，阴影半径扩大的倍率")]
    public float heightScaleFactor = 0.25f;

    [Tooltip("阴影最大半径限制")]
    public float maxRadius = 0.9f;

    [Header("光源方向偏移（模拟 COTL 身后影子）")]
    [Tooltip("是否根据主方向光把阴影向单位身后偏移")]
    public bool offsetByLightDir = true;

    [Tooltip("阴影沿光源反方向的偏移强度")]
    public float lightOffsetScale = 0.35f;

    [Tooltip("光源方向造成的椭圆压扁/拉伸强度")]
    [Range(0f, 1f)] public float lightSquash = 0.35f;

    [Tooltip("找不到主光源时的默认光照方向（与当前场景假光一致）")]
    public Vector3 defaultLightDir = new Vector3(0.4f, 1f, 0.3f);

    [Header("高度淡出")]
    [Tooltip("阴影开始明显淡出时的高度")]
    public float fadeStartHeight = 0.3f;

    [Tooltip("阴影完全消失的高度")]
    public float fadeEndHeight = 1.5f;

    [Tooltip("整体透明度缩放")]
    [Range(0f, 1f)] public float alphaMultiplier = 0.7f;

    [Tooltip("阴影 quad 相对地面的抬高（避免 z-fighting）")]
    public float groundOffset = 0.03f;

    [Tooltip("射线检测的额外起始高度（从单位上方一点开始向下射，避免自身碰撞体遮挡）")]
    public float raycastTopOffset = 0.5f;

    [Tooltip("射线最大长度")]
    public float raycastMaxDistance = 3f;

    private Transform shadowTransform;
    private MeshRenderer shadowRenderer;
    private MaterialPropertyBlock mpb;
    private static readonly int s_Color = Shader.PropertyToID("_BaseColor");
    private static readonly int s_Radius = Shader.PropertyToID("_Radius");
    private static readonly int s_Softness = Shader.PropertyToID("_Softness");
    private static Material s_DefaultMaterial;

    void Awake()
    {
        EnsureShadowObject();
        mpb = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        EnsureShadowObject();
        if (shadowRenderer != null) shadowRenderer.enabled = true;
    }

    void OnDisable()
    {
        if (shadowRenderer != null) shadowRenderer.enabled = false;
    }

    void LateUpdate()
    {
        if (shadowTransform == null) EnsureShadowObject();
        if (shadowTransform == null) return;

        UpdateShadowPositionAndScale();
    }

    private void EnsureShadowObject()
    {
        if (shadowTransform != null) return;

        // 查找或创建阴影子物体
        Transform existing = transform.Find("BlobShadow");
        if (existing != null)
        {
            shadowTransform = existing;
            shadowRenderer = existing.GetComponent<MeshRenderer>();
            return;
        }

        GameObject go = new GameObject("BlobShadow", typeof(MeshFilter), typeof(MeshRenderer));
        go.hideFlags = HideFlags.HideAndDontSave;
        go.layer = gameObject.layer;
        shadowTransform = go.transform;
        shadowTransform.SetParent(transform, false);
        shadowTransform.localPosition = Vector3.zero;

        MeshFilter mf = go.GetComponent<MeshFilter>();
        mf.sharedMesh = BuildQuadMesh();

        shadowRenderer = go.GetComponent<MeshRenderer>();
        shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        shadowRenderer.receiveShadows = false;
        shadowRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        shadowRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // 若未指定材质，加载默认材质（仅加载一次并缓存）
        if (shadowMaterial == null)
        {
            if (s_DefaultMaterial == null)
            {
                s_DefaultMaterial = Resources.Load<Material>("Materials/M_BlobShadow");
#if UNITY_EDITOR
                // 编辑器下 Resources 可能尚未完成导入，直接走 AssetDatabase 兜底
                if (s_DefaultMaterial == null)
                {
                    s_DefaultMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Materials/M_BlobShadow.mat");
                }
#endif
            }
            shadowMaterial = s_DefaultMaterial;
        }

        if (shadowMaterial == null)
        {
            Debug.LogWarning(
                $"[BlobShadow] {gameObject.name}: 未找到材质 Resources/Materials/M_BlobShadow 或 " +
                "Assets/Resources/Materials/M_BlobShadow.mat，请在 Inspector 中赋值 shadowMaterial。", this);
        }
        else
        {
            shadowRenderer.sharedMaterial = shadowMaterial;
        }
    }

    private static Mesh BuildQuadMesh()
    {
        Mesh mesh = new Mesh();
        mesh.vertices = new Vector3[]
        {
            new Vector3(-0.5f, 0f, -0.5f),
            new Vector3(0.5f, 0f, -0.5f),
            new Vector3(-0.5f, 0f, 0.5f),
            new Vector3(0.5f, 0f, 0.5f)
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateBounds();
        return mesh;
    }

    private Vector3 GetMainLightDirXZ()
    {
        Vector3 lightDir;
        if (RenderSettings.sun != null)
        {
            lightDir = -RenderSettings.sun.transform.forward;
        }
        else
        {
#if UNITY_2023_1_OR_NEWER
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            Light[] lights = FindObjectsOfType<Light>();
#endif
            Light dirLight = null;
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional && l.enabled)
                {
                    dirLight = l;
                    break;
                }
            }
            lightDir = dirLight != null ? -dirLight.transform.forward : defaultLightDir.normalized;
        }
        return lightDir;
    }

    private void UpdateShadowPositionAndScale()
    {
        Vector3 origin = transform.position + Vector3.up * raycastTopOffset;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, raycastMaxDistance, groundMask);
        RaycastHit hit = default;
        bool found = false;
        foreach (var h in hits)
        {
            // 跳过自身及子物体的碰撞体，避免把单位自己当成地面
            if (h.transform == transform || h.transform.IsChildOf(transform)) continue;
            hit = h;
            found = true;
            break;
        }

        if (found)
        {
            float height = Mathf.Max(0f, transform.position.y - hit.point.y);
            float t = Mathf.InverseLerp(fadeStartHeight, fadeEndHeight, height);
            float alpha = Mathf.Lerp(1f, 0f, t) * alphaMultiplier;

            float radius = Mathf.Min(baseRadius + height * heightScaleFactor, maxRadius);

            // 计算光源方向（XZ 平面）
            Vector3 lightDir = GetMainLightDirXZ();
            Vector3 lightDirH = new Vector3(lightDir.x, 0f, lightDir.z);
            float lightHorizLen = lightDirH.magnitude;
            Vector3 lightDirHNorm = lightHorizLen > 0.001f ? lightDirH / lightHorizLen : Vector3.forward;

            // 阴影位置：向光源反方向偏移（影子落在身后）
            Vector3 posOffset = Vector3.zero;
            if (offsetByLightDir && lightHorizLen > 0.001f)
            {
                posOffset = -lightDirHNorm * (height * lightOffsetScale + radius * 0.2f);
            }

            // 椭圆：沿光源水平反方向拉伸，垂直方向略微压窄
            Vector3 scale = new Vector3(radius * 2f, radius * 2f, 1f);
            if (offsetByLightDir && lightHorizLen > 0.001f)
            {
                float stretch = 1f + lightSquash * Mathf.Clamp01(lightHorizLen);
                float squash = 1f - lightSquash * 0.25f * Mathf.Clamp01(lightHorizLen);
                // 沿光源反方向为 x，垂直光源方向为 y（quad 本地坐标）
                scale.x = radius * 2f * stretch;
                scale.y = radius * 2f * squash;

                // 旋转 quad 让拉伸方向对准光源反方向
                float angle = Mathf.Atan2(lightDirHNorm.x, lightDirHNorm.z) * Mathf.Rad2Deg;
                shadowTransform.rotation = Quaternion.Euler(90f, angle + 90f, 0f);
            }
            else
            {
                shadowTransform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // 阴影始终水平贴地
            shadowTransform.position = hit.point + Vector3.up * groundOffset + posOffset;
            shadowTransform.localScale = scale;

            if (shadowRenderer != null)
            {
                shadowRenderer.enabled = alpha > 0.01f && shadowMaterial != null;
                if (shadowRenderer.enabled)
                {
                    shadowRenderer.GetPropertyBlock(mpb);
                    Color c = shadowMaterial.GetColor(s_Color);
                    c.a = alpha;
                    mpb.SetColor(s_Color, c);
                    mpb.SetFloat(s_Radius, shadowMaterial.GetFloat(s_Radius));
                    mpb.SetFloat(s_Softness, shadowMaterial.GetFloat(s_Softness));
                    shadowRenderer.SetPropertyBlock(mpb);
                }
            }
        }
        else
        {
            if (shadowRenderer != null) shadowRenderer.enabled = false;
        }
    }

    void OnDestroy()
    {
        if (shadowTransform != null)
        {
            if (Application.isPlaying) Destroy(shadowTransform.gameObject);
            else DestroyImmediate(shadowTransform.gameObject);
        }
    }
}
