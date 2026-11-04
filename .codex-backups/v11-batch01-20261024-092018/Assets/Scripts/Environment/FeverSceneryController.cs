using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MusicalSprite.Terrain
{
    /// <summary>每方三层布景：背景常驻，过热与超级过热独立升降并同时存在。</summary>
    [DisallowMultipleComponent]
    public sealed class FeverSceneryController : MonoBehaviour
    {
        [Range(0, 1)] public int side;
        public Transform background;
        public Transform feverLayer;
        public Transform superFeverLayer;
        [Min(0.01f)] public float riseDuration = 0.5f;
        [Min(0.01f)] public float fallDuration = 0.45f;
        public Vector3 feverHiddenLocalPosition;
        public Vector3 feverShownLocalPosition;
        public Vector3 superHiddenLocalPosition;
        public Vector3 superShownLocalPosition;
        [Header("Temporary artwork framing")]
        [Range(0.1f, 1f)] public float visibleImageHeight = 0.30f;
        public Color feverTint = new Color(1f, 0.48f, 0.3f, 1f);
        public Color superTint = new Color(1f, 0.78f, 0.35f, 1f);

        private FeverManager manager;
        private Coroutine riseAnimation;
        private Sprite croppedBackground;
        private Sprite croppedFever;
        private Sprite croppedSuper;
        private readonly List<GameObject> backdropObjects = new List<GameObject>();
        private readonly List<Mesh> backdropMeshes = new List<Mesh>();
        private readonly List<Material> backdropMaterials = new List<Material>();

        private void Awake()
        {
            // Also cover an already-open editor scene whose serialized instance has
            // not reloaded from disk since the framing change.
            if (Mathf.Abs(feverShownLocalPosition.y - 2.4f) < 0.01f &&
                Mathf.Abs(feverHiddenLocalPosition.y + 4f) < 0.01f)
            {
                feverShownLocalPosition.y = 2.25f;
                feverHiddenLocalPosition.y = -4.8f;
                superShownLocalPosition.y = 1.5f;
                superHiddenLocalPosition.y = -4.8f;
            }
            if (side == 1 && feverTint.r > 0.9f && feverTint.b < 0.4f)
            {
                feverTint = new Color(0.31f, 0.61f, 1f, 1f);
                superTint = new Color(0.47f, 0.91f, 1f, 1f);
            }
            ReframeScale(background);
            ReframeScale(feverLayer);
            ReframeScale(superFeverLayer);
            FaceMainCamera(background);
            FaceMainCamera(feverLayer);
            FaceMainCamera(superFeverLayer);
            // Current placeholders have artwork only in their bottom 28%. Frame that area
            // without changing the source textures, so each layer can rise visibly.
            croppedBackground = FrameSprite(background);
            croppedFever = FrameSprite(feverLayer);
            croppedSuper = FrameSprite(superFeverLayer);
            SetOrder(background, -70);
            SetOrder(feverLayer, -60);
            SetOrder(superFeverLayer, -50);
            TintLayer(feverLayer, feverTint);
            TintLayer(superFeverLayer, superTint);
            CreateBackdrop(feverLayer, false);
            CreateBackdrop(superFeverLayer, true);
        }

        private void CreateBackdrop(Transform layer, bool super)
        {
            if (layer == null) return;
            var shader = Resources.Load<Shader>("Shaders/LaneGraphic");
            if (shader == null) return;
            var panel = new GameObject(super ? "SuperFever_PaintedBackdrop" : "Fever_PaintedBackdrop");
            panel.transform.SetParent(layer, false);
            panel.transform.localPosition = new Vector3(0f, 0f, 0.035f);
            var mesh = BuildBackdrop(side == 1, super);
            panel.AddComponent<MeshFilter>().sharedMesh = mesh;
            var material = new Material(shader) { renderQueue = 3001 };
            material.SetColor("_Tint", super
                ? side == 0 ? new Color(0.59f, 0.42f, 0.43f, 1f) : new Color(0.48f, 0.53f, 0.66f, 1f)
                : side == 0 ? new Color(0.37f, 0.27f, 0.35f, 1f) : new Color(0.31f, 0.33f, 0.47f, 1f));
            var renderer = panel.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = super ? -51 : -61;
            backdropObjects.Add(panel);
            backdropMeshes.Add(mesh);
            backdropMaterials.Add(material);
        }

        private static Mesh BuildBackdrop(bool rightSide, bool super)
        {
            const int segments = 24;
            var vertices = new List<Vector3>((segments + 1) * 4);
            var colors = new List<Color>(vertices.Capacity);
            var triangles = new List<int>(segments * 18);
            for (int row = 0; row < 4; row++)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float towardOuterEdge = rightSide ? t : 1f - t;
                    float rise = towardOuterEdge * towardOuterEdge * (3f - 2f * towardOuterEdge);
                    float contour = 0.11f * Mathf.Sin(t * 14f) + 0.065f * Mathf.Sin(t * 31f);
                    float top = (super ? 1.15f : 1.4f) + (super ? 1.25f : 1.55f) * rise + contour;
                    float y = row == 0 ? 0f : row == 1 ? top * 0.47f : row == 2 ? top : top + 0.24f;
                    vertices.Add(new Vector3(Mathf.Lerp(-5.35f, 5.35f, t), y, 0f));
                    float alpha = row == 3 ? 0f : row == 2 ? 0.72f : 0.82f;
                    float edgeFade = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) * 14f);
                    colors.Add(new Color(1f, 1f, 1f, alpha * edgeFade));
                }
            }
            for (int row = 0; row < 3; row++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a = row * (segments + 1) + i;
                    int b = a + segments + 1;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
            }
            var mesh = new Mesh { name = super ? "SuperFeverBackdropShape" : "FeverBackdropShape" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void FaceMainCamera(Transform layer)
        {
            if (layer == null || Camera.main == null) return;
            // A sprite's front is its local -Z. Match the angled battle camera so
            // its painted face, rather than an edge, is shown in Game view.
            layer.rotation = Camera.main.transform.rotation;
        }

        private static void ReframeScale(Transform layer)
        {
            if (layer == null || layer.localScale.x > 0.65f) return;
            layer.localScale = new Vector3(0.76f, 1.32f, 1f);
        }

        private static void SetOrder(Transform layer, int order)
        {
            if (layer == null) return;
            var renderer = layer.GetComponent<SpriteRenderer>();
            if (renderer != null) renderer.sortingOrder = order;
        }

        private Sprite FrameSprite(Transform layer)
        {
            if (layer == null) return null;
            var renderer = layer.GetComponent<SpriteRenderer>();
            if (renderer == null || renderer.sprite == null) return null;
            var source = renderer.sprite;
            var rect = source.rect;
            float height = Mathf.Max(1f, rect.height * visibleImageHeight);
            var framed = Sprite.Create(source.texture,
                new Rect(rect.x, rect.y, rect.width, height),
                new Vector2(0.5f, 0f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            framed.name = source.name + "_VisibleBottom";
            renderer.sprite = framed;
            return framed;
        }

        private static void TintLayer(Transform layer, Color tint)
        {
            if (layer == null) return;
            var renderer = layer.GetComponent<SpriteRenderer>();
            if (renderer == null) return;
            renderer.color = tint;
            // These layers are scenery, not territory props. Keep their alpha and let
            // the ground geometry cover their lower edge naturally.
        }

        private IEnumerator Start()
        {
            if (feverLayer != null) feverLayer.localPosition = feverHiddenLocalPosition;
            if (superFeverLayer != null) superFeverLayer.localPosition = superHiddenLocalPosition;
            while (FeverManager.Instance == null) yield return null;
            manager = FeverManager.Instance;
            manager.OnStateChanged += OnStateChanged;
            ApplyImmediately(manager.GetState(side));
        }

        private void OnDestroy()
        {
            if (manager != null) manager.OnStateChanged -= OnStateChanged;
            if (croppedBackground != null) Destroy(croppedBackground);
            if (croppedFever != null) Destroy(croppedFever);
            if (croppedSuper != null) Destroy(croppedSuper);
            foreach (var backdrop in backdropObjects) if (backdrop != null) Destroy(backdrop);
            foreach (var mesh in backdropMeshes) if (mesh != null) Destroy(mesh);
            foreach (var material in backdropMaterials) if (material != null) Destroy(material);
        }

        private void OnStateChanged(FeverState oldState, FeverState state, int changedSide)
        {
            if (changedSide != side) return;
            if (riseAnimation != null) StopCoroutine(riseAnimation);
            riseAnimation = StartCoroutine(MoveTo(state));
        }

        private void ApplyImmediately(FeverState state)
        {
            if (feverLayer != null)
                feverLayer.localPosition = state == FeverState.None ? feverHiddenLocalPosition : feverShownLocalPosition;
            if (superFeverLayer != null)
                superFeverLayer.localPosition = state == FeverState.SuperFever ? superShownLocalPosition : superHiddenLocalPosition;
        }

        // 临时图片可能完全相同；这些菜单可在 Inspector 直接检查三层位置，
        // 无需先打出连击。编辑模式预览后选 Normal 可恢复隐藏位置。
        [ContextMenu("Preview/Normal")]
        private void PreviewNormal() => Preview(FeverState.None);

        [ContextMenu("Preview/Fever")]
        private void PreviewFever() => Preview(FeverState.Fever);

        [ContextMenu("Preview/Super Fever")]
        private void PreviewSuperFever() => Preview(FeverState.SuperFever);

        private void Preview(FeverState state)
        {
            if (Application.isPlaying)
            {
                if (riseAnimation != null) StopCoroutine(riseAnimation);
                riseAnimation = StartCoroutine(MoveTo(state));
            }
            else ApplyImmediately(state);
        }

        private IEnumerator MoveTo(FeverState state)
        {
            Vector3 feverFrom = feverLayer != null ? feverLayer.localPosition : Vector3.zero;
            Vector3 superFrom = superFeverLayer != null ? superFeverLayer.localPosition : Vector3.zero;
            Vector3 feverTo = state == FeverState.None ? feverHiddenLocalPosition : feverShownLocalPosition;
            Vector3 superTo = state == FeverState.SuperFever ? superShownLocalPosition : superHiddenLocalPosition;
            float duration = state == FeverState.None ? fallDuration : riseDuration;
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.deltaTime)
            {
                float k = Mathf.Clamp01(elapsed / duration);
                k = 1f - Mathf.Pow(1f - k, 3f);
                if (feverLayer != null) feverLayer.localPosition = Vector3.LerpUnclamped(feverFrom, feverTo, k);
                if (superFeverLayer != null) superFeverLayer.localPosition = Vector3.LerpUnclamped(superFrom, superTo, k);
                yield return null;
            }
            if (feverLayer != null) feverLayer.localPosition = feverTo;
            if (superFeverLayer != null) superFeverLayer.localPosition = superTo;
            riseAnimation = null;
        }
    }
}
