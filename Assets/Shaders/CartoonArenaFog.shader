Shader "MusicalSprite/CartoonArenaFog"
{
    Properties
    {
        _FogTint ("Outside Color", Color) = (0.008, 0.008, 0.035, 1)
        _FogStrength ("Outside Opacity", Range(0, 1)) = 0.96
        _DriftSpeed ("Edge Drift", Range(0, 0.4)) = 0.035
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "Arena Edge Fog"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _FogTint;
            float _FogStrength;
            float _DriftSpeed;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float SoftNoise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash21(cell), Hash21(cell + float2(1, 0)), f.x);
                float b = lerp(Hash21(cell + float2(0, 1)), Hash21(cell + 1), f.x);
                return lerp(a, b, f.y);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                // A painted silhouette around the small island. Screen-space masking
                // also covers the sky/background where there is no depth to sample.
                float2 drift = float2(_Time.y * _DriftSpeed, -_Time.y * _DriftSpeed * 0.61);
                float broad = SoftNoise(uv * float2(9.0, 13.0) + drift);
                float detail = SoftNoise(uv * float2(24.0, 21.0) - drift * 0.4);
                float wobble = (broad - 0.5) * 0.022 + (detail - 0.5) * 0.006;
                float backEdge = 0.725 + wobble;
                float back = smoothstep(backEdge - 0.025, backEdge + 0.04, uv.y);
                float sideInset = lerp(0.045, 0.012, smoothstep(0.03, 0.58, uv.y));
                float left = 1.0 - smoothstep(sideInset - 0.018 + wobble,
                                               sideInset + 0.022 + wobble, uv.x);
                float right = smoothstep(1.0 - sideInset - 0.022 + wobble,
                                         1.0 - sideInset + 0.018 + wobble, uv.x);
                float outside = max(back, max(left, right));
                // Retain faint silhouettes outside while suppressing flat grey and
                // invalid bright background pixels.
                float luminance = dot(sceneColor.rgb, float3(0.21, 0.72, 0.07));
                float3 darkSilhouette = _FogTint.rgb + saturate(luminance) * 0.022;
                sceneColor.rgb = lerp(sceneColor.rgb, darkSilhouette,
                                      saturate(outside * _FogStrength));
                return sceneColor;
            }
            ENDHLSL
        }
    }
}
