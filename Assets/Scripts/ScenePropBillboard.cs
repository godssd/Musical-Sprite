using UnityEngine;

namespace MusicalSprite
{
    /// <summary>
    /// Makes a scene-prop "stand up" by always facing the main camera.
    /// Mirrors the COTL-style "立着背景图" feel without requiring a true 3D model:
    /// one sprite billboard is enough for a fixed-camera 2.5D game.
    ///
    /// Usage:
    /// - Attach to any GameObject that has a SpriteRenderer (or just a child sprite).
    /// - The billboard rotates around Y so the local +Z points at the camera.
    /// - The optional _tiltAroundX lets you lean the prop slightly forward
    ///   (useful for 2D backgrounds that should "lean into" the camera).
    /// - It also keeps a "stretch by distance" off, so silhouettes stay stable.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class ScenePropBillboard : MonoBehaviour
    {
        public enum BillboardMode
        {
            YAxis,          // only rotate around Y (most common: trees, signs)
            Full,           // free rotation, faces the camera dead-on (posters, cards)
        }

        [Tooltip("How the billboard rotates to face the camera.")]
        public BillboardMode mode = BillboardMode.YAxis;

        [Tooltip("If false, the billboard stops auto-rotating and you can manually set the angle in the Inspector / Scene view.")]
        public bool enableBillboard = true;

        [Tooltip("Optional Yaw offset in degrees. Useful for fine-tuning the prop angle even when billboard is on.")]
        [Range(-180f, 180f)]
        public float yAngleOffset = 0f;

        [Tooltip("Optional: lean the prop forward / backward in degrees. " +
                 "Useful when you want a background image to feel like it 'reaches' the camera.")]
        [Range(-60f, 60f)]
        public float tiltAroundX = 0f;

        [Tooltip("If true, applies the billboard to this transform. " +
                 "If false, look for a child named 'SpriteChild' (SpriteRenderer child) " +
                 "and only rotate that. Lets the prop have a separate root for movement.")]
        public bool rotateThisObject = true;

        [Tooltip("When 'rotateThisObject' is false, name of the child to rotate. " +
                 "Empty = first child with a SpriteRenderer.")]
        public string spriteChildName = "";

        private Transform _target;
        private Camera _cam;

        private void OnEnable()
        {
            ResolveTarget();
            ResolveCamera();
        }

        private void ResolveTarget()
        {
            if (rotateThisObject)
            {
                _target = transform;
                return;
            }

            if (!string.IsNullOrEmpty(spriteChildName))
            {
                _target = transform.Find(spriteChildName);
            }
            else
            {
                // First child with a SpriteRenderer.
                foreach (Transform child in transform)
                {
                    if (child.GetComponent<SpriteRenderer>() != null)
                    {
                        _target = child;
                        break;
                    }
                }
            }
        }

        private void ResolveCamera()
        {
            _cam = Camera.main;
        }

        private void LateUpdate()
        {
            if (_target == null)
            {
                ResolveTarget();
                if (_target == null) return;
            }

            if (!enableBillboard) return;

            if (_cam == null)
            {
                ResolveCamera();
                if (_cam == null) return;
            }

            Vector3 toCam = _cam.transform.position - _target.position;

            switch (mode)
            {
                case BillboardMode.YAxis:
                {
                    // Project to XZ plane.
                    toCam.y = 0f;
                    if (toCam.sqrMagnitude < 0.0001f) return;
                    _target.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                    break;
                }
                case BillboardMode.Full:
                {
                    if (toCam.sqrMagnitude < 0.0001f) return;
                    _target.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
                    break;
                }
            }

            // Apply optional yaw offset and tilt.
            _target.rotation *= Quaternion.Euler(tiltAroundX, yAngleOffset, 0f);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_target == null) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(_target.position, _target.position + _target.forward * 1.5f);
        }
#endif
    }
}
