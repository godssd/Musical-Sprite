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
        _OutlineWidth("描边宽度 (世界单位)", Range(0, 0.1)) = 0.03
        _OutlineColor("描边颜色", Color) = (0.05, 0.05, 0.08, 1)
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

            // SRP Batcher 约束：所有 5 个 Pass 的 UnityPerMaterial 布局必须完全一致。
            // 此处使用「全属性并集」。_OutlineDoodle 已废弃（描边不再做顶点抖动），
            // 仅保留成员占位以维持布局与旧材质序列化数据兼容。
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
        // Pass 2 : Inverted Hull Outline（稳定版）
        // 修复记录（2026-09-11）：
        // 旧实现把 Doodle 抖动与法线偏移直接加在裁剪空间坐标上
        // （posCS.xy += offset * positionCS.w），抖动量随距离放大且每帧随机，
        // 导致描边壳整片乱飞（黑色不稳定方块）并随机切片盖住模型本体（模型被截断）。
        // 现改为：视空间沿法线外扩固定世界宽度，无任何逐帧随机位移；
        // Doodle 手绘感由 ForwardLit 的 UV 采样抖动独立承担。
        // ============================================================
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Offset 1, 1

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
            };

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

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                // 视空间沿法线外扩固定世界宽度：偏移量不随距离缩放、不随帧变化。
                float3 positionVS = posInputs.positionVS;
                float3 normalVS   = normalize(mul((float3x3)UNITY_MATRIX_V, normalInputs.normalWS));
                positionVS += normalVS * _OutlineWidth;

                output.positionCS = mul(UNITY_MATRIX_P, float4(positionVS, 1.0));
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // ============================================================
        // Pass 3 : ShadowCaster —— 写入主光阴影贴图。
        // 缺失后果：套用本 shader 的物体不投射实时阴影，
        // 地面（GroundEdge 采样 MainLightRealtimeShadow）收不到它们的影子。
        // ============================================================
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

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

            float3 _LightDirection;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                float4 positionCS = TransformWorldToHClip(
                    ApplyShadowBias(posInputs.positionWS, normalInputs.normalWS, _LightDirection));

                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ============================================================
        // Pass 4 : DepthOnly —— 写入相机深度纹理（深度 prepass / 移动端路径）。
        // ============================================================
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Back
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthOnlyAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthOnlyVaryings
            {
                float4 positionCS : SV_POSITION;
            };

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

            DepthOnlyVaryings DepthOnlyVert(DepthOnlyAttributes input)
            {
                DepthOnlyVaryings output;
                output.positionCS = GetVertexPositionInputs(input.positionOS.xyz).positionCS;
                return output;
            }

            half DepthOnlyFrag(DepthOnlyVaryings input) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        // ============================================================
        // Pass 5 : DepthNormals —— 写入深度 + 法线（PC 端 SSAO prepass 用）。
        // 缺失后果：PC_Renderer 上的 SSAO 看不到套用本 shader 的物体。
        // ============================================================
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

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

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.normalWS = normalize(TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_TARGET
            {
                return half4(PackNormal(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
