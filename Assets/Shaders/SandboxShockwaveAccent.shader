Shader "MusicalSprite/SandboxShockwaveAccent"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+5" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS:POSITION; float4 color:COLOR; };
            struct Varyings { float4 positionHCS:SV_POSITION; float4 color:COLOR; };
            Varyings vert(Attributes v) { Varyings o; o.positionHCS=TransformObjectToHClip(v.positionOS); o.color=v.color; return o; }
            half4 frag(Varyings i):SV_Target { return half4(i.color.rgb, i.color.a); }
            ENDHLSL
        }
    }
}
