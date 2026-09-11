using UnityEngine;

namespace MusicalSprite.Terrain
{
    /// <summary>
    /// 地形配置存档：换地形时按这份配置一键输出地面 / 玩家台面的材质与贴图。
    /// 地面（圆角矩形）与台面（圆盘）各自独立配置；草边遮罩逻辑不变（遮罩决定流苏形状）。
    /// 配套编辑器工具：Tools/Musical-Sprite/Terrain/Apply Selected Terrain Profile
    /// </summary>
    [CreateAssetMenu(fileName = "NewTerrainProfile", menuName = "Musical-Sprite/Terrain Profile")]
    public class TerrainProfileSO : ScriptableObject
    {
        [Header("地面（圆角矩形模式）")]
        public Texture2D groundRedMap;
        public Texture2D groundBlueMap;
        [Tooltip("草边遮罩模板（GroundEdge_Template）")]
        public Texture2D grassMask;
        public Color groundTint = Color.white;

        [Header("地面 中线 / 草边")]
        public float centerLineX = -1.44f;
        public float centerBlend = 0.15f;
        public float edgeOutset = 0.23f;
        public float edgeTiling = 19f;
        public float edgeVerticalScale = 0.94f;
        [Range(0f, 1f)] public float edgeCutoff = 0.05f;
        public float edgeOverhang = 0.08f;

        [Header("地面 形状")]
        public Vector4 groundMin = new Vector4(-8f, -3.75f, 0f, 0f);
        public Vector4 groundMax = new Vector4(8f, 3.75f, 0f, 0f);
        public float cornerRadius = 2f;
        public float groundThickness = 0.8f;

        [Header("地面 光照 / 阴影")]
        public Color sideColor = new Color(0.45f, 0.32f, 0.22f, 1f);
        public Color shadowColor = new Color(0.35f, 0.32f, 0.45f, 1f);
        [Range(0f, 1f)] public float shadowIntensity = 0.55f;
        [Range(0f, 1f)] public float shadowSoftness = 0.35f;

        [Header("草边描边（地面 + 台面共用）")]
        public Color outlineColor = new Color(0.05f, 0.05f, 0.08f, 1f);
        [Tooltip("描边宽度（世界单位），与 ToonDoodle 方块描边同级")]
        public float outlineWidth = 0.03f;

        [Header("玩家台面（圆盘模式）")]
        [Tooltip("左侧台面贴图（当前 Forest：yuantai_1_A）")]
        public Texture2D stageMapLeft;
        [Tooltip("右侧台面贴图（当前 Forest：yuantai_1_B）")]
        public Texture2D stageMapRight;
        [Tooltip("台面草边外扩宽度（世界单位，画在台面边界内侧）")]
        public float stageEdgeOutset = 0.23f;
        public float stageEdgeTiling = 4f;
        public float stageEdgeVerticalScale = 0.94f;
        [Range(0f, 1f)] public float stageEdgeCutoff = 0.05f;

        [Header("台面 圆心 / 半径")]
        [Tooltip("自动从场景 LeftBand_Stage / RightBand_Stage 的物体原点取圆心（半圆盘圆心=物体原点）")]
        public bool autoStageCenterFromScene = true;
        [Tooltip("自动从渲染器包围盒取半径（半圆盘/圆盘均适用：半径=最大 XZ 半尺寸）")]
        public bool autoStageRadiusFromScene = true;
        [Tooltip("自动关闭时的手动圆心（XZ 生效）——左")]
        public Vector4 stageCenterLeftOverride = Vector4.zero;
        [Tooltip("自动关闭时的手动圆心（XZ 生效）——右")]
        public Vector4 stageCenterRightOverride = Vector4.zero;
        [Tooltip("自动关闭时的手动半径（左右共用）")]
        public float stageRadiusManual = 1.2f;
    }
}
