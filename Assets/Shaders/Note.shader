Shader "MusicalSprite/Note"
{
    // 音符专用 shader（替代原来的 URP/Lit）。
    // 为什么不用 URP/Lit：本项目场景全是 Unlit / 假光照，URP 灯光照不到音符，
    // 但冲击波染色走的是 Shader.SetGlobal* 全局参数 —— Lit 不认这套参数，所以音符「不吃光」。
    // 本 shader 与 ScenePropSprite / GrassFringe / GroundEdge 用同一套受光代码，观感才会一致。
    //
    // 三件事：
    //   1) 卡通竖直渐变：底部偏暗、顶部偏亮（局部 Y 归一化），不做真实法线光照
    //   2) 冲击波双光源受光（红蓝各自按距离衰减后相加）—— 与场景 shader 完全同款
    //   3) 保留 URP 雾（FOG_LINEAR/EXP/EXP2）与深度写入，雾的观感不变
    Properties
    {
        _BaseColor  ("Base Color", Color) = (1, 0.9, 0.2, 1)
        _TopTint    ("Top Tint", Color) = (1, 1, 0.88, 1)
        _BottomTint ("Bottom Tint", Color) = (0.62, 0.5, 0.14, 1)
        _ShadePower ("Shade Curve Power", Float) = 1.0
        _Ambient    ("Ambient", Range(0, 2)) = 1.0
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        Cull Back
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ FOG_LINEAR FOG_EXP FOG_EXP2
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _BaseColor, _TopTint, _BottomTint;
            float  _ShadePower, _Ambient;

            // 冲击波辉光：全局参数（Shader.SetGlobal*），故意放在 CBUFFER 之外。
            // 放进去会被归入 UnityPerMaterial，与 SetGlobal 写入冲突（SRP Batcher 逐材质常量）。
            float  _ShockGlowEnabled;
            half4  _ShockGlowColorRed;
            half4  _ShockGlowColorBlue;
            float4 _ShockGlowParamsRed;   // xyz = 红墙世界位置, w = 影响半径
            float4 _ShockGlowParamsBlue;  // xyz = 蓝墙世界位置, w = 影响半径
            float4 _ShockGlowStrengths;   // x = 红强度, y = 蓝强度

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS      : TEXCOORD0;   // 冲击波距离衰减需要世界坐标
                float  shade      : TEXCOORD1;   // 0=底 1=顶
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.posWS = posWS;
                // Unity 内置 Cube 的局部 y ∈ [-0.5, 0.5]；+0.5 归一化到 0..1
                o.shade = pow(saturate(v.positionOS.y + 0.5), max(0.01, _ShadePower));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 tint = lerp(_BottomTint.rgb, _TopTint.rgb, i.shade);
                half3 col  = _BaseColor.rgb * tint * _Ambient;

                // ---- 冲击波辉光染色（与场景 shader 同款双光源）----
                if (_ShockGlowEnabled > 0.5)
                {
                    float dR = distance(i.posWS, _ShockGlowParamsRed.xyz);
                    float aR = saturate(1.0 - dR / max(0.0001, _ShockGlowParamsRed.w));
                    aR = aR * aR;
                    col += _ShockGlowColorRed.rgb * (aR * _ShockGlowStrengths.x);

                    float dB = distance(i.posWS, _ShockGlowParamsBlue.xyz);
                    float aB = saturate(1.0 - dB / max(0.0001, _ShockGlowParamsBlue.w));
                    aB = aB * aB;
                    col += _ShockGlowColorBlue.rgb * (aB * _ShockGlowStrengths.y);
                }

                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                col = MixFog(col, ComputeFogFactor(i.positionCS.z));
                #endif

                return half4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}
