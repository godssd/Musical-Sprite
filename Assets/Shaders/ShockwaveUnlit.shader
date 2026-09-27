Shader "MusicalSprite/ShockwaveUnlit"
{
    // P1 占位 shader：URP 透明双面，颜色取自顶点色（含 alpha 渐变）。
    // 后续 P2 会在此基础扩展贴图 / UV 滚动 / 边缘光 / 单侧色相渐变，
    // 但顶点色驱动的"从判定线淡入"渐变方向在 P1 先验证。
    Properties
    {
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 color       : COLOR;
                float2 uv          : TEXCOORD0;
            };

            half4 _Color;

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                o.color = IN.color;
                o.uv = IN.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 c = IN.color * _Color;
                return c;
            }
            ENDHLSL
        }
    }
}
