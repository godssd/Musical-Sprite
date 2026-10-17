using UnityEngine;

namespace MusicalSprite.Terrain
{
    /// <summary>玩家外观插槽。摆放姿态保存在场景对象中，换图只更换此处引用。</summary>
    [CreateAssetMenu(fileName = "PlayerWorldVisualSet", menuName = "Musical-Sprite/Player World Visual Set")]
    public sealed class PlayerWorldVisualSetSO : ScriptableObject
    {
        [Header("已接入的世界外观")]
        public Texture2D groundMap;
        public Texture2D stageMap;
        public Sprite[] sceneProps;
        public Sprite normalBackground;
        public Sprite feverScenery;
        public Sprite superFeverScenery;

        [Header("后续逐项接入的外观插槽（不改变玩法判定）")]
        public Object judgementLineSkin;
        public Object laneIndicatorSkin;
        public Object noteSkin;
    }
}
