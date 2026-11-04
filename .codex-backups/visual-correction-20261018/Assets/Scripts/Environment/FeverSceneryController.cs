using System.Collections;
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
            renderer.sharedMaterial = null;
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
            renderer.sharedMaterial = null;
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
