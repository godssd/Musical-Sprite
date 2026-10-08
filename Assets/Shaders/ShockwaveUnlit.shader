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
        _FadePower        ("Alpha Fade Power", Float) = 1.4
        _GradientPower    ("Color Gradient Power", Float) = 1.0
        _GradientBalance  ("Deep/Tip Balance", Range(0,1)) = 0.5
        _EdgeGlow         ("Edge Glow", Float) = 0.18
        _Opacity        ("Opacity", Range(0,1)) = 0.40
        _Flash          ("Flash Intensity", Range(0,1)) = 0

        // ---- P1b 拱形结构渐变（A）+ 厚度 core（B）----
        _ZHalf            ("Z Half Range (auto)", Float) = 3.75
        _ArchPower        ("Arch Structure Power", Range(0.1, 4)) = 1
        _ArchFocus        ("Arch Focus (0=edges, 1=crest)", Range(0,1)) = 1
        _ArchAmount       ("Arch Color Amount", Range(0,1)) = 0
        _ShellIntensity   ("Shell Thickness Intensity", Range(0,2)) = 0

        // ---- P1 发光层（独立 Additive Pass 专用）----
        _GlowColor      ("Glow Color", Color) = (1.00, 0.55, 0.62, 1)
        _GlowIntensity  ("Glow Intensity", Float) = 1.6
        _GlowArch       ("Glow Arch Focus", Float) = 2.0
        _GlowFront      ("Glow Front Focus", Float) = 1.6
        _GlowTailCut    ("Glow Tail Cut", Range(0,1)) = 0.15
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
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float  t           : TEXCOORD1;   // 0=back(判定线) 1=front(中缝)
                float  vy          : TEXCOORD2;   // 0=接地 1=拱顶
                float  vz          : TEXCOORD3;   // 0=拱顶中央(z=0) ±1=z边缘
            };

            half4  _ColorDeep, _ColorTip, _ColorTail;
            float4 _MainTex_ST;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float2 _ScrollSpeed;
            float  _BackX, _FrontX, _ArchHeight, _ZHalf, _FadePower, _GradientPower, _GradientBalance, _EdgeGlow, _Opacity, _Flash;
            float  _ArchPower, _ArchFocus, _ArchAmount, _ShellIntensity;
            float  _TailPoint, _TailWidth;
            float  _VerticalAmount, _VerticalCenter, _VerticalWidth, _VerticalPower;

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                o.uv = IN.uv;

                float span = max(0.0001, abs(_FrontX - _BackX));
                o.t  = clamp((IN.positionOS.x - _BackX) / span, 0.0, 1.0);
                o.vy = clamp(IN.positionOS.y / max(0.0001, _ArchHeight), 0.0, 1.0);
                o.vz = clamp(IN.positionOS.z / max(0.0001, _ZHalf), -1.0, 1.0);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t  = IN.t;
                float vy = IN.vy;
                float vz = IN.vz;

                // ---- A+B 拱形结构：s = |z/zHalf|，0=拱顶中央，1=z边缘接地 ----
                float s = abs(vz);
                float powAP = max(0.01, _ArchPower);
                float archMask = pow(1.0 - s, powAP);   // 拱顶浓
                float edgeMask = pow(s, powAP);          // 两端浓
                float structureBlend = lerp(edgeMask, archMask, _ArchFocus);
                float core = archMask;                   // 拱顶厚、两端薄

                // 1) alpha 淡入：判定线透明，中缝最浓
                float alpha = pow(t, _FadePower) * _Opacity;

                // 2) 色相渐变：尾端深 -> 判定线深 -> 中缝亮（3 层）
                float curved_t = pow(t, _GradientPower);

                // Tail -> Deep：0.._TailPoint 区间
                float tailToDeep = smoothstep(saturate(_TailPoint - _TailWidth),
                                               saturate(_TailPoint + _TailWidth),
                                               curved_t);
                half3 col = lerp(_ColorTail.rgb, _ColorDeep.rgb, tailToDeep);

                // Deep -> Tip：由 _GradientBalance 控制过渡中心
                float deepCenter = 1.0 - _GradientBalance;
                float deepToTip = smoothstep(saturate(deepCenter - 0.25),
                                             saturate(deepCenter + 0.25),
                                             curved_t);
                col = lerp(col, _ColorTip.rgb, deepToTip);

                // A) 拱形结构色：让 Deep/Tip 渐变也顺着 z 弧度走
                half3 structureColor = lerp(_ColorDeep.rgb, _ColorTip.rgb, structureBlend);
                col = lerp(col, structureColor, _ArchAmount);

                // C) 纵向渐变（自下而上）：默认中心在地面附近，向上淡出
                if (_VerticalAmount > 0.001)
                {
                    float vDist = abs(vy - _VerticalCenter) / max(0.001, _VerticalWidth);
                    float vMask = saturate(1.0 - vDist);
                    vMask = pow(max(vMask, 0.0), max(0.01, _VerticalPower));
                    // 默认：中心（贴地附近）最亮，向两侧衰减 -> 能量从地面升起
                    col *= lerp(1.0, vMask * 1.5 + 0.5, _VerticalAmount);
                }

                // 可选贴图（默认白=无效果）：随时间滚动
                float2 uvT = IN.uv * _MainTex_ST.xy + _Time.y * _ScrollSpeed;
                half4 tex  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvT);
                col   *= tex.rgb;
                alpha *= tex.a;

                // 3) 边缘光：拱顶(vy->1) + 前沿(t->1)
                //    B) core 调制：拱顶更亮、两端更收，产生厚度/体积感
                float crest = pow(vy, 3.0);
                float front = pow(t, 3.0);
                float edgeGlowMod = lerp(1.0, core, _ShellIntensity);
                col += _EdgeGlow * (crest + front) * _ColorTip.rgb * edgeGlowMod;

                // B) alpha 也按 core 收边：默认 _ShellIntensity=0 时完全不变
                alpha *= lerp(1.0, saturate(core * 1.2), _ShellIntensity * 0.5);

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

        // ============================================================================
        // P1 发光层：独立 Additive Pass（Blend One One），只在拱顶 / 前沿叠加超过 1 的高亮，
        // 由 Bloom 泛出光晕。主体 Pass 的表现完全不受影响；把 _GlowIntensity 设为 0 即等于关闭。
        // 坐标空间与主体 Pass 严格一致（positionOS），请勿改成世界坐标。
        // ============================================================================
        Pass
        {
            Name "GlowAdditive"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vertGlow
            #pragma fragment fragGlow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AttributesGlow
            {
                float3 positionOS : POSITION;
            };

            struct VaryingsGlow
            {
                float4 positionHCS : SV_POSITION;
                float  t           : TEXCOORD0;   // 0=判定线 1=中缝（与主体 Pass 同定义）
                float  vy          : TEXCOORD1;   // 0=接地   1=拱顶
                float  vz          : TEXCOORD2;   // 0=拱顶中央 ±1=z边缘
            };

            half4 _GlowColor;
            float  _GlowIntensity, _GlowArch, _GlowFront, _GlowTailCut;
            float  _BackX, _FrontX, _ArchHeight, _ZHalf, _Flash;
            float  _ArchPower, _ArchFocus, _ArchAmount, _ShellIntensity;
            float  _TailPoint, _TailWidth;
            float  _VerticalAmount, _VerticalCenter, _VerticalWidth, _VerticalPower;

            VaryingsGlow vertGlow(AttributesGlow IN)
            {
                VaryingsGlow o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                float span = max(0.0001, abs(_FrontX - _BackX));
                o.t  = clamp((IN.positionOS.x - _BackX) / span, 0.0, 1.0);
                o.vy = clamp(IN.positionOS.y / max(0.0001, _ArchHeight), 0.0, 1.0);
                o.vz = clamp(IN.positionOS.z / max(0.0001, _ZHalf), -1.0, 1.0);
                return o;
            }

            half4 fragGlow(VaryingsGlow IN) : SV_Target
            {
                if (_GlowIntensity <= 0.001 && _Flash <= 0.001) discard;

                // 身后（判定线侧）渐隐，避免整条墙糊满光
                float tail  = smoothstep(_GlowTailCut, min(1.0, _GlowTailCut + 0.35), IN.t);
                float crest = pow(IN.vy, max(0.01, _GlowArch));    // 拱顶聚拢
                float front = pow(IN.t,  max(0.01, _GlowFront));   // 前沿聚拢
                float mask  = saturate(crest * 0.75 + front) * tail + _Flash * 0.8;

                // B) core 厚度：拱顶中央更亮，z 边缘收光
                float s = abs(IN.vz);
                float core = pow(1.0 - s, max(0.01, _ArchPower));
                mask *= lerp(1.0, core, _ShellIntensity);

                half3 g = _GlowColor.rgb * _GlowIntensity * mask;
                return half4(g, 1.0);
            }
            ENDHLSL
        }
    }
}
