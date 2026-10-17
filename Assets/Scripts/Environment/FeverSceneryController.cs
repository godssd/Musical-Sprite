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

        private FeverManager manager;
        private Coroutine animation;

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
        }

        private void OnStateChanged(FeverState oldState, FeverState state, int changedSide)
        {
            if (changedSide != side) return;
            if (animation != null) StopCoroutine(animation);
            animation = StartCoroutine(MoveTo(state));
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
                if (animation != null) StopCoroutine(animation);
                animation = StartCoroutine(MoveTo(state));
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
            animation = null;
        }
    }
}
