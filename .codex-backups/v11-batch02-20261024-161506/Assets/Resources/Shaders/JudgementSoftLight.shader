Shader "MusicalSprite/JudgementSoftLight"
{
    Properties
    {
        _NearColor ("Near colour", Color) = (1,0.34,0.30,1)
        _FarColor ("Far colour", Color) = (0.60,0.49,0.78,1)
        _CoreColor ("Core colour", Color) = (1,0.94,0.84,1)
        _Strength ("Strength", Range(0,1)) = 1
        _HitMix ("Hit mix", Range(0,1)) = 0
        [HideInInspector] _Kind ("Shape kind", Float) = 0
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha [_DstBlend]
        Pass
        {
            Name "SoftJudgement"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _NearColor;
                float4 _FarColor;
                float4 _CoreColor;
                float _Strength;
                float _HitMix;
                float _Kind;
                float _DstBlend;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }
            float EndFeather(float t, float width)
            {
                return smoothstep(0.0, width, t) * (1.0 - smoothstep(1.0 - width, 1.0, t));
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = saturate(input.uv);
                float u = uv.x;
                float across = uv.y * 2.0 - 1.0;
                float tail = pow(1.0 - u, 1.55) * (1.0 - smoothstep(0.70, 1.0, u));
                float3 tint = lerp(_NearColor.rgb, _FarColor.rgb, smoothstep(0.04, 0.9, u) * 0.78);
                float alpha;
                float3 color;
                if (_Kind < 0.5)
                {
                    float2 aa = max(fwidth(uv), float2(0.001, 0.001));
                    alpha = EndFeather(u, aa.x) * EndFeather(uv.y, aa.y) * 0.90;
                    color = _NearColor.rgb;
                }
                else if (_Kind < 1.5)
                {
                    alpha = tail * exp2(-5.0 * across * across) * 0.18;
                    color = lerp(float3(0.79, 0.77, 0.70), _NearColor.rgb, 0.24);
                }
                else if (_Kind < 2.5)
                {
                    float lateral = u * 2.0 - 1.0;
                    float core = exp2(-125.0 * lateral * lateral);
                    float halo = max(0.0, exp2(-5.5 * lateral * lateral) - exp2(-5.5)) * 0.26;
                    alpha = (core * 0.96 + halo) * EndFeather(uv.y, 0.045) * _Strength;
                    color = lerp(lerp(float3(1,1,1), _NearColor.rgb, saturate(_HitMix)),
                        _CoreColor.rgb, core);
                }
                else if (_Kind < 3.5)
                {
                    float taper = max(0.3, 1.0 - u * 0.62);
                    float d = across / taper;
                    float core = exp2(-190.0 * d * d);
                    float halo = max(0.0, exp2(-5.0 * d * d) - exp2(-5.0));
                    alpha = (core * 0.68 + halo * 0.30) * tail * _Strength;
                    color = lerp(tint, _CoreColor.rgb, core * (1.0 - u));
                }
                else
                {
                    float nearFade = smoothstep(0.0, 0.025, u);
                    float soft = max(0.0, exp2(-2.5 * across * across) - exp2(-2.5));
                    alpha = nearFade * tail * soft * _Strength * lerp(0.14, 0.21, _HitMix);
                    color = lerp(float3(0.92,0.94,0.98), tint, saturate(_HitMix));
                }
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
