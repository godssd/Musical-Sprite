using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MusicalSprite.FX
{
    /// <summary>
    /// Animates the abyss fog layers as a group:
    /// - Each child layer gently floats up and down on Y (sin, per-layer phase).
    /// - Each child layer sways slightly on Z rotation.
    /// - The parent object slowly rotates around Y so the noise doesn't read as
    ///   "stuck to the camera".
    /// Run only on the parent GameObject that holds the fog layers as children.
    /// </summary>
    [DisallowMultipleComponent]
    public class AbyssFogAnimator : MonoBehaviour
    {
        [Header("Float (per layer)")]
        [Tooltip("Y oscillation amplitude in world units. Kept small so the thin layers stay interleaved (the main wave motion comes from the shader vertex wave).")]
        public float floatAmplitude = 0.06f;
        [Tooltip("Y oscillation speed.")]
        public float floatSpeed = 0.5f;
        [Tooltip("Extra phase offset between layers (radians per layer index).")]
        public float layerPhaseOffset = 0.7f;

        [Header("Sway (per layer)")]
        [Tooltip("Z rotation amplitude in degrees.")]
        public float swayAngle = 1.5f;
        [Tooltip("Sway speed.")]
        public float swaySpeed = 0.3f;

        [Header("Group rotation")]
        [Tooltip("Y rotation speed in degrees/sec for the whole group. Keep 0 so the fog stays locked under the platform.")]
        public float groupRotateSpeed = 0f;

        private Vector3[]   m_BasePos;
        private Quaternion[] m_BaseRot;

        private void Awake()
        {
            CacheBaseTransforms();
        }

        private void CacheBaseTransforms()
        {
            int n = transform.childCount;
            m_BasePos  = new Vector3[n];
            m_BaseRot  = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var c = transform.GetChild(i);
                m_BasePos[i] = c.localPosition;
                m_BaseRot[i] = c.localRotation;
            }
        }

        private void LateUpdate()
        {
            // Group rotation (parent) — independent of per-layer sway.
            transform.Rotate(0f, groupRotateSpeed * Time.deltaTime, 0f, Space.World);

            if (m_BasePos == null || m_BasePos.Length != transform.childCount)
            {
                CacheBaseTransforms();
            }

            float t = Time.time;
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                float phase = i * layerPhaseOffset;

                // Float
                float dy = Mathf.Sin(t * floatSpeed + phase) * floatAmplitude;
                c.localPosition = m_BasePos[i] + new Vector3(0f, dy, 0f);

                // Sway
                float sway = Mathf.Sin(t * swaySpeed + phase * 0.7f) * swayAngle;
                c.localRotation = m_BaseRot[i] * Quaternion.Euler(0f, 0f, sway);
            }
        }
    }
}
