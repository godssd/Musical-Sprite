Shader "MusicalSprite/ShockwaveUnlit"
{
    // P2 冲击波 shader（URP 17 / Unity 6）。
    // 三件事同时满足：
    //   1) 单侧 alpha 淡入：判定线侧(t=0)完全透明，中缝侧(t=1)最浓  -> 满足"判定线消失"
    //   2) 色相渐变：判定线深(_ColorDeep) -> 中缝亮(_ColorTip)        -> 满足"自身单侧颜色渐变"
    //   3) 边缘光：拱顶(vy->1) + 前沿(t->1) 提亮
    // 可选 _MainTex（手绘能量纹，默认白=无效果）；UV 随时间滚动。
    // t / vy 由物体局部坐标与 _BackX/_FrontX/_ArchHeight 推导，墙移动时 shader 自动跟随。
    Properties
    {
        _ColorDeep      ("Color Deep (judge line)", Color) = (0.42, 0.05, 0.10, 1)
        _ColorTip       ("Color Tip (clash front)", Color) = (1.00, 0.55, 0.62, 1)
        _MainTex        ("Energy Pattern", 2D) = "white" {}
        _ScrollSpeed    ("UV Scroll (xy)", Vector) = (0.05, 0.0, 0, 0)
        _BackX          ("Back X (judge line)", Float) = -6
        _FrontX         ("Front X (seam)", Float) = 0
        _ArchHeight     ("Arch Height", Float) = 1.2
        _ZHalf          ("Z Half Range", Float) = 3.75
        _BaseY          ("World Base Y (bottom of wall)", Float) = 0
        _FadePower        ("Alpha Fade Power", Float) = 1.4
        _GradientPower    ("Color Gradient Power", Float) = 1.0
        _GradientBalance  ("Deep/Tip Balance", Range(0,1)) = 0.5
        _EdgeGlow         ("Edge Glow", Float) = 0.18
        _Opacity        ("Opacity", Range(0,1)) = 0.40
        _Flash          ("Flash Intensity", Range(0,1)) = 0
        // ---- 发光层（第二个 Additive Pass）----
        _GlowColor      ("Glow Color", Color) = (1.00, 0.55, 0.62, 1)
        _GlowIntensity  ("Glow Intensity", Float) = 1.5
        _GlowArch       ("Glow Arch Power", Float) = 3.0
        _GlowFront      ("Glow Front Power", Float) = 3.0
        _GlowTailCut    ("Glow Tail Cut", Range(0,1)) = 0.25
        // ---- 消散（从两侧与身后边缘侵蚀）----
        _Dissolve       ("Dissolve Amount", Range(0,1)) = 0
        _NoiseAmount    ("Noise Amount (0=off)", Range(0,1)) = 0
        [Toggle(_DEBUG_SOLID)] _DebugSolid ("Debug Solid Red", Float) = 0
        [Toggle(_FORCE_VISIBLE)] _ForceVisible ("Force Visible", Float) = 0
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
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local_fragment _ _DEBUG_SOLID _FORCE_VISIBLE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  t           : TEXCOORD1;   // 0=back(判定线) 1=front(中缝)
                float  vy          : TEXCOORD2;   // 0=接地 1=拱顶
                float  edge        : TEXCOORD3;   // 0=中央 1=z 两侧边缘（消散用）
            };

            half4  _ColorDeep, _ColorTip;
            float4 _MainTex_ST;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float2 _ScrollSpeed;
            float  _BackX, _FrontX, _ArchHeight, _BaseY, _FadePower, _GradientPower, _GradientBalance, _EdgeGlow, _Opacity, _Flash;
            float  _ZHalf;
            half4  _GlowColor;
            float  _GlowIntensity, _GlowArch, _GlowFront, _GlowTailCut, _Dissolve, _NoiseAmount;

            // 消散遮罩：0=完全保留，1=完全消失。
            // 侵蚀顺序（用户指定）：从【身后 backX 侧】与【两侧 z 边缘】开始，向中缝 / 中央推进。
            // 重要：中缝头部（t->1）与拱顶必须受保护，否则头部会出现噪点斑块。
            float DissolveMask(float t, float vy, float edge, float2 uv)
            {
                if (_Dissolve <= 0.001) return 1.0;

                // 基础侵蚀源：身后（t 小）为主，两侧边缘次之，顶部只在身后轻微参与
                float fromBack = 1.0 - t;
                float fromSide = edge * (1.0 - t * 0.7);       // 头部两侧几乎不侵蚀
                float fromTop  = vy * 0.08 * (1.0 - t);        // 顶部仅在身后轻微侵蚀

                float erode = saturate(fromBack * 0.85 + fromSide * 0.30 + fromTop * 0.10);

                // 头部 / 拱顶保护区：t 越大、越靠中央、越靠拱顶，越难被侵蚀
                float protect = pow(t, 0.45) * (1.0 - edge * 0.25) * (1.0 - vy * 0.15);
                erode = saturate(erode - protect * 1.6);

                // 噪声扰动（防硬边）。_NoiseAmount=0 时完全关闭，避免斑驳噪点。
                float n = frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);
                erode = saturate(erode + (n - 0.5) * _NoiseAmount);

                return 1.0 - smoothstep(erode - 0.20, erode + 0.08, _Dissolve);
            }

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                o.uv = IN.uv;

                // 关键：导入模型经过 autoFit 缩放后，positionOS 仍是原始局部坐标，
                // 而 _BackX/_FrontX/_ArchHeight/_ZHalf 是世界空间概念。必须用世界坐标计算。
                float3 worldPos = TransformObjectToWorld(IN.positionOS);
                float span = max(0.0001, abs(_FrontX - _BackX));
                o.t    = clamp((worldPos.x - _BackX) / span, 0.0, 1.0);
                o.vy   = clamp((worldPos.y - _BaseY) / max(0.0001, _ArchHeight), 0.0, 1.0);
                o.edge = clamp(abs(worldPos.z) / max(0.0001, _ZHalf), 0.0, 1.0);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                #if defined(_DEBUG_SOLID)
                return half4(1.0, 0.0, 0.0, 1.0);
                #endif

                float t  = IN.t;
                float vy = IN.vy;

                #if defined(_FORCE_VISIBLE)
                return half4(_ColorTip.rgb, 1.0);
                #endif

                // 1) alpha 淡入：判定线透明，中缝最浓
                float alpha = pow(t, _FadePower) * _Opacity * DissolveMask(t, vy, IN.edge, IN.uv);

                // 2) 色相渐变：判定线深 -> 中缝亮
                //    _GradientBalance 控制 Deep/Tip 的比重：
                //      0 = Deep 占绝大部分（Tip 只在前沿很小一块）
                //      1 = Tip 占绝大部分（Deep 只在根部很小一块）
                //      0.5 = 各占约一半，过渡带在墙中部
                float curved_t = pow(t, _GradientPower);
                float transition_center = 1.0 - _GradientBalance;
                float half_width = 0.25;
                float gradient_t = smoothstep(saturate(transition_center - half_width),
                                                 saturate(transition_center + half_width),
                                                 curved_t);
                half3 col = lerp(_ColorDeep.rgb, _ColorTip.rgb, gradient_t);

                // 可选贴图（默认白=无效果）：随时间滚动
                float2 uvT = IN.uv * _MainTex_ST.xy + _Time.y * _ScrollSpeed;
                half4 tex  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvT);
                col   *= tex.rgb;
                alpha *= tex.a;

                // 3) 边缘光：拱顶(vy->1) + 前沿(t->1)
                float crest = pow(vy, 3.0);
                float front = pow(t, 3.0);
                col += _EdgeGlow * (crest + front) * _ColorTip.rgb;

                // 4) 补分闪烁：_Flash>0 时提亮 + 增饱和（饱和度向自身灰度外推）
                if (_Flash > 0.001)
                {
                    half lum = dot(col, half3(0.299, 0.587, 0.114));
                    col = lerp(half3(lum, lum, lum), col, 1.0 + _Flash * 1.5); // 增饱和
                    col = col + _Flash * 0.6;                                   // 提亮
                    col = saturate(col);
                }

                return half4(col, alpha);
            }
            ENDHLSL
        }

        // ---- 第二个 Pass：Additive 发光层 ----
        // 目的：让冲击波自身"发光"（配合 URP Bloom 产生泛光），并对周围形成光效。
        // 主体 Pass 保持半透明观感不变，本 Pass 只在拱顶 / 前沿叠加超出 1.0 的高亮，
        // Blend One One 叠加，不影响主体透明度。渲染顺序在同一 Renderer 内排主体之后。
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local_fragment _ _DEBUG_SOLID _FORCE_VISIBLE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  t           : TEXCOORD1;
                float  vy          : TEXCOORD2;
            };

            half4  _GlowColor;
            float  _BackX, _FrontX, _ArchHeight, _BaseY;
            float  _GlowIntensity, _GlowArch, _GlowFront, _GlowTailCut;
            float  _Flash, _Opacity, _Dissolve;

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                o.uv = IN.uv;
                float3 worldPos = TransformObjectToWorld(IN.positionOS);
                float span = max(0.0001, abs(_FrontX - _BackX));
                o.t  = clamp((worldPos.x - _BackX) / span, 0.0, 1.0);
                o.vy = clamp((worldPos.y - _BaseY) / max(0.0001, _ArchHeight), 0.0, 1.0);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                #if defined(_DEBUG_SOLID)
                return half4(1.0, 0.0, 0.0, 1.0);
                #endif

                float t  = IN.t;
                float vy = IN.vy;

                #if defined(_FORCE_VISIBLE)
                return half4(_ColorTip.rgb, 1.0);
                #endif

                // 身后（判定线侧）裁掉一段，避免发光糊满整条墙
                float tail = smoothstep(_GlowTailCut, min(1.0, _GlowTailCut + 0.45), t);
                float crest = pow(vy, max(0.01, _GlowArch));   // 拱顶
                float front = pow(t,  max(0.01, _GlowFront));  // 前沿（中缝）
                float mask = saturate(crest * 0.65 + front) * tail;

                // 消散时发光同步减弱（与主体一致，避免"消失了还在发光"）
                mask *= (1.0 - _Dissolve);

                // 闪白时整墙额外迸亮
                mask += _Flash * 0.5;

                half3 g = _GlowColor.rgb * _GlowIntensity * mask;
                return half4(g, 0.0);   // Additive：只加 rgb，不污染 alpha
            }
            ENDHLSL
        }
    }
}
