using System.Collections.Generic;
using UnityEngine;

namespace MusicalSprite.Terrain
{
    /// <summary>Small world-space mist at the rear edge of the arena.</summary>
    public sealed class ArenaBoundaryMist : MonoBehaviour
    {
        private Mesh mistMesh;
        private Material mistMaterial;

        public static void Ensure()
        {
            if (FindFirstObjectByType<ArenaBoundaryMist>() != null) return;
            var go = new GameObject("ArenaBoundaryMist_WorldSpace");
            go.AddComponent<ArenaBoundaryMist>();
        }

        private void Awake()
        {
            var shader = Resources.Load<Shader>("Shaders/BoundaryMist");
            if (shader == null)
            {
                Debug.LogError("[ArenaBoundaryMist] BoundaryMist shader is missing.");
                enabled = false;
                return;
            }

            mistMesh = BuildMist();
            var filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mistMesh;
            mistMaterial = new Material(shader) { renderQueue = 3002 };
            mistMaterial.SetColor("_Tint", new Color(0.24f, 0.22f, 0.34f, 1f));
            var renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mistMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Mesh BuildMist()
        {
            const int segments = 48;
            float[] rows = { 3.25f, 3.65f, 4.25f, 5.15f };
            float[] alpha = { 0f, 0.18f, 0.34f, 0.08f };
            var vertices = new List<Vector3>((segments + 1) * rows.Length);
            var colors = new List<Color>(vertices.Capacity);
            var triangles = new List<int>(segments * (rows.Length - 1) * 6);
            for (int row = 0; row < rows.Length; row++)
            {
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float x = Mathf.Lerp(-8.4f, 8.4f, t);
                    float scallop = Mathf.Sin(x * 1.15f) * 0.10f + Mathf.Sin(x * 2.4f) * 0.035f;
                    vertices.Add(new Vector3(x, 0.085f, rows[row] + scallop));
                    float fade = Mathf.SmoothStep(0f, 1f, Mathf.Min(t, 1f - t) * 12f);
                    colors.Add(new Color(1f, 1f, 1f, alpha[row] * fade));
                }
            }
            for (int row = 0; row < rows.Length - 1; row++)
            {
                for (int i = 0; i < segments; i++)
                {
                    int a = row * (segments + 1) + i;
                    int b = a + segments + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(b); triangles.Add(b + 1);
                }
            }
            var mesh = new Mesh { name = "ArenaRearMist" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void OnDestroy()
        {
            if (mistMesh != null) Destroy(mistMesh);
            if (mistMaterial != null) Destroy(mistMaterial);
        }
    }
}
