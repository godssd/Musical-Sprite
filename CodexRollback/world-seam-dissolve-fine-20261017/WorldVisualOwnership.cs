using UnityEngine;

namespace MusicalSprite.Terrain
{
    /// <summary>场景件归属。A/B 按世界坐标逐像素跟随地面；其他类型不受中缝影响。</summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class WorldVisualOwnership : MonoBehaviour
    {
        public enum Kind { Neutral = -1, PlayerA = 0, PlayerB = 1, FeverA = 2, FeverB = 3 }
        public Kind kind = Kind.Neutral;

        private static readonly int TerritorySide = Shader.PropertyToID("_TerritorySide");
        private MaterialPropertyBlock block;

        private void OnEnable() => Apply();
        private void OnValidate() => Apply();

        public void Apply()
        {
            if (block == null) block = new MaterialPropertyBlock();
            float side = kind == Kind.PlayerA ? 0f : kind == Kind.PlayerB ? 1f : -1f;
            foreach (var sprite in GetComponentsInChildren<SpriteRenderer>(true))
            {
                sprite.GetPropertyBlock(block);
                block.SetFloat(TerritorySide, side);
                sprite.SetPropertyBlock(block);
            }
        }
    }
}
