using UnityEngine;

namespace MusicalSprite.Terrain
{
    /// <summary>把 A/B 玩家外观配置接到现有地面、圆台材质；不接管中缝和玩法。</summary>
    [DisallowMultipleComponent]
    public sealed class ArenaWorldVisualBinder : MonoBehaviour
    {
        public PlayerWorldVisualSetSO playerA;
        public PlayerWorldVisualSetSO playerB;
        public BattleCenterLine centerLine;
        public Renderer leftStage;
        public Renderer rightStage;

        private void Start()
        {
            if (centerLine == null || centerLine.groundMaterial == null) return;
            // 运行时材质副本避免把调整写回美术原材质。
            var ground = new Material(centerLine.groundMaterial);
            centerLine.groundMaterial = ground;
            if (centerLine.ground != null && centerLine.ground.TryGetComponent(out Renderer groundRenderer))
                groundRenderer.sharedMaterial = ground;
            if (playerA != null && playerA.groundMap != null) ground.SetTexture("_RedMap", playerA.groundMap);
            if (playerB != null && playerB.groundMap != null) ground.SetTexture("_BlueMap", playerB.groundMap);

            ApplyStage(leftStage, playerA);
            ApplyStage(rightStage, playerB);
        }

        private static void ApplyStage(Renderer renderer, PlayerWorldVisualSetSO set)
        {
            if (renderer == null || set == null || set.stageMap == null || renderer.sharedMaterial == null) return;
            var material = new Material(renderer.sharedMaterial);
            material.SetTexture("_StageMap", set.stageMap);
            renderer.sharedMaterial = material;
        }
    }
}
