Shader "MusicalSprite/ToonDoodle"
{
    Properties
    {
        [MainTexture] _BaseMap("基础贴图", 2D) = "white" {}
        [MainColor]   _BaseColor("基础色", Color) = (1,1,1,1)

        [Header(Doodle Jitter)]
        [Toggle(_DOODLE_ON)] _Doodle("启用 Doodle 抖动", Float) = 1
        _DoodleSize("抖动尺寸 (UV 噪声频率)", Float) = 8
        _DoodleSpeed("抖动速度", Float) = 9
        _DoodleIntensity("抖动强度", Range(0, 0.05)) = 0.005

        [Header(Toon Shading)]
        _StepCount("色阶层数", Range(1, 8)) = 4
        _ShadowThreshold("阴影阈值", Range(0, 1)) = 0.35
        _ShadowColor("阴影色", Color) = (0.35, 0.3, 0.45, 1)
        _HighlightThreshold("高光阈值", Range(0, 1)) = 0.75
        _HighlightColor("高光色", Color) = (1.1, 1.05, 0.95, 1)

        [Header(Outline)]
        _OutlineWidth("描边宽度", Range(0, 0.1)) = 0.02
        _OutlineColor("描边颜色", Color) = (0.05, 0.05, 0.08, 1)
        _OutlineDoodle("描边也抖动", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        LOD 100

        // ============================================================
        // Pass 1 : ForwardLit + Toon + Doodle
        // ============================================================
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ FOG_LINEAR FOG_EXP FOG_EXP2
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS

            #pragma shader_feature_local _DOODLE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // SRP Batcher 约束：所有 Pass 的 UnityPerMaterial 布局必须完全一致。
            // 此处使用「全属性并集」，ForwardLit / Outline 两 Pass 共用，避免 SRP Batcher 失效。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _DoodleSize;
                float  _DoodleSpeed;
                float  _DoodleIntensity;
                float  _StepCount;
                float  _ShadowThreshold;
                float4 _ShadowColor;
                float  _HighlightThreshold;
                float4 _HighlightColor;
                float  _OutlineWidth;
                float4 _OutlineColor;
                float  _OutlineDoodle;
            CBUFFER_END

            // 简单的 2D 噪声，用于 Doodle 抖动
            float2 hash22(float2 p)
            {
                float3 p3  = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float2 doodleOffset(float2 uv, float time)
            {
                float2 grid = floor(uv * _DoodleSize);
                float2 rnd  = hash22(grid + floor(time * _DoodleSpeed));
                // 把噪声映射到 [-1,1] 再乘强度
                return (rnd * 2 - 1) * _DoodleIntensity;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS   = normalInputs.normalWS;
                output.uv         = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                #ifdef _DOODLE_ON
                uv += doodleOffset(uv, _Time.y);
                #endif

                float4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                float3 baseColor = baseMap.rgb * _BaseColor.rgb;
                float  alpha     = baseMap.a * _BaseColor.a;

                // 简单的 Lambert 光照
                #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #else
                float4 shadowCoord = float4(0, 0, 0, 0);
                #endif

                Light mainLight = GetMainLight(shadowCoord);
                float NdotL = saturate(dot(normalize(input.normalWS), mainLight.direction));
                NdotL *= mainLight.shadowAttenuation;

                // Toon 阶梯
                float litBand = floor(NdotL * _StepCount) / _StepCount;

                // 阴影/高光混合
                float3 shadowed = baseColor * _ShadowColor.rgb;
                float3 highlighted = baseColor * _HighlightColor.rgb;

                float3 toonColor = lerp(shadowed, baseColor, litBand);
                toonColor = lerp(toonColor, highlighted, saturate((NdotL - _HighlightThreshold) * 4));

                float3 finalColor = toonColor * mainLight.color;

                // 雾
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float fogCoord = ComputeFogFactor(input.positionCS.z);
                finalColor = MixFog(finalColor, fogCoord);
                #endif

                return float4(finalColor, alpha);
            }
            ENDHLSL
        }

        // ============================================================
        // Pass 2 : Inverted Hull Outline
        // ============================================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Offset 0, 1   // 稍微推远，避免正面自身遮挡

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma shader_feature_local _DOODLE_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            // 与 ForwardLit 完全一致的全属性并集（SRP Batcher 约束）。
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor;
                float  _DoodleSize;
                float  _DoodleSpeed;
                float  _DoodleIntensity;
                float  _StepCount;
                float  _ShadowThreshold;
                float4 _ShadowColor;
                float  _HighlightThreshold;
                float4 _HighlightColor;
                float  _OutlineWidth;
                float4 _OutlineColor;
                float  _OutlineDoodle;
            CBUFFER_END

            float2 hash22(float2 p)
            {
                float3 p3  = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            float2 doodleOffset(float2 uv, float time)
            {
                float2 grid = floor(uv * _DoodleSize);
                float2 rnd  = hash22(grid + floor(time * _DoodleSpeed));
                return (rnd * 2 - 1) * _DoodleIntensity;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                float3 normalWS = normalInputs.normalWS;
                float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_V, normalWS));
                float2 offset   = normalVS.xy * _OutlineWidth * posInputs.positionCS.w;

                float4 posCS = posInputs.positionCS;
                posCS.xy += offset;

                #ifdef _DOODLE_ON
                float2 uv = TRANSFORM_TEX(input.uv, _BaseMap);
                float2 dOffset = doodleOffset(uv, _Time.y);
                posCS.xy += dOffset * _OutlineDoodle * posInputs.positionCS.w;
                #endif

                output.positionCS = posCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
