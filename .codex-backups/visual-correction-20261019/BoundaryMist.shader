Shader "MusicalSprite/BoundaryMist"
{
    Properties { _Tint ("Mist Color", Color) = (0.035, 0.026, 0.072, 1) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _Tint;
            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float wave : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                float wave = sin(i.positionOS.x * 0.72 + _Time.y * 0.36)
                           + 0.35 * sin(i.positionOS.x * 1.77 - _Time.y * 0.23);
                i.positionOS.z += 0.12 * wave;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.color = i.color * _Tint;
                o.wave = wave;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                i.color.a *= 0.85 + 0.15 * i.wave;
                return i.color;
            }
            ENDHLSL
        }
    }
}
