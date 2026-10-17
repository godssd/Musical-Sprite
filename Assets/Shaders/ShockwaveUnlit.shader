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

        // ---- P3 表面流体（分形杂色 + 漩涡 + 上下拉长 + 沿推进方向流动；★实验项，_FlowAmount=0 时完全等同于旧版）----
        _FlowAmount     ("Flow Amount", Range(0,1)) = 0
        _FlowScale      ("Flow Scale", Float) = 3
        _FlowSpeed      ("Flow Speed", Float) = 0.35
        _FlowWarp       ("Flow Warp", Range(0,2)) = 0.6
        _FlowDetail     ("Flow Detail (2nd octave)", Range(0,1)) = 0.5
        _FlowColorAmt   ("Flow Color Amount", Range(0,1)) = 0.5
        _FlowAlphaAmt   ("Flow Alpha Amount", Range(0,1)) = 0.6
        _FlowStretchX   ("Flow Stretch X (flow axis, pull long)", Float) = 1
        _FlowStretchY   ("Flow Stretch Y (height, pull long)", Float) = 4
        _FlowSwirl      ("Flow Swirl", Range(0,2)) = 0.6
        // 0 = 采样 x 轴用纵深 vz（旧版，只能上下/纵深流动）；1 = 用"从中缝向己方半场"的长度坐标(1-t)
        _FlowLengthMix  ("Flow Axis: 0=depth vz, 1=length outward", Range(0,1)) = 1
        // ★方向 = 图案看起来的移动方向。x:+1 = 从中缝往己方半场（与拖尾同向），y:+1 = 向上
        _FlowScrollDir  ("Flow Scroll Dir (+x outward, +y up)", Vector) = (1, 0, 0, 0)

        // ---- P3 尾部溶解（同源的漩涡分形杂色作为侵蚀图，判定线侧 t→0 被柔和烧蚀；★实验项，_DissolveAmount=0 时完全等同于旧版）----
        _DissolveAmount      ("Dissolve Amount", Range(0,1)) = 0
        _DissolvePoint       ("Dissolve Center (t)", Range(0,1)) = 0.18
        _DissolveWidth       ("Dissolve Width (t)", Range(0.01,1)) = 0.25
        _DissolveEdge        ("Dissolve Edge Softness", Range(0.001,0.5)) = 0.08
        _DissolveScale       ("Dissolve Noise Scale", Float) = 4
        _DissolveSpeed       ("Dissolve Noise Speed", Float) = 0.15
        _DissolveStretchX    ("Dissolve Stretch X (depth vz)", Float) = 1
        _DissolveStretchY    ("Dissolve Stretch Y (height, pull long)", Float) = 4
        _DissolveSwirl       ("Dissolve Swirl", Range(0,2)) = 0.6
        _DissolveScrollDir   ("Dissolve Scroll Dir (x y)", Vector) = (0, -1, 0, 0)
        _DissolveEdgeColor   ("Dissolve Edge Color", Color) = (1.60, 0.90, 0.60, 1)
        _DissolveEdgeIntensity ("Dissolve Edge Intensity", Float) = 0.8
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
            #include "ShockwaveNoise.hlsl"

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
            half4  _DissolveEdgeColor;
            float  _FlowAmount, _FlowScale, _FlowSpeed, _FlowWarp, _FlowDetail, _FlowColorAmt, _FlowAlphaAmt;
            float  _FlowStretchX, _FlowStretchY, _FlowSwirl, _FlowLengthMix;
            float4 _FlowScrollDir;
            float  _DissolveAmount, _DissolvePoint, _DissolveWidth, _DissolveEdge, _DissolveScale;
            float  _DissolveSpeed, _DissolveEdgeIntensity;
            float  _DissolveStretchX, _DissolveStretchY, _DissolveSwirl;
            float4 _DissolveScrollDir;
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

                // ---- P3 表面流体：沿墙推进方向流动的长条状涡状浓淡 ----
                // ★ _FlowAmount=0 时整段跳过，输出与旧版逐位一致（主游戏默认零影响）
                // 采样坐标 x 轴 = lerp(纵深 vz, "从中缝往己方半场"的长度 1-t, _FlowLengthMix)；y 轴 = 拱高 vy
                //   （y 再被 _FlowStretchY 拉长 ⇒ 上下拉得很长的漩涡；_FlowScrollDir(+x) 让它从中缝往己方半场流）
                if (_FlowAmount > 0.001)
                {
                    float2 fp = float2(lerp(vz, 1.0 - t, _FlowLengthMix), vy) * _FlowScale;
                    float fn = ShockFlowNoise(fp, _Time.y, _FlowSpeed,
                                              _FlowWarp * _FlowAmount, _FlowDetail,
                                              _FlowStretchX, _FlowStretchY, _FlowSwirl * _FlowAmount,
                                              _FlowScrollDir.x, _FlowScrollDir.y);
                    // 明暗对比拉开，让流动看得见：颜色波动 0.60~1.40，alpha 波动 0.50~1.30
                    col   *= lerp(1.0, 0.60 + fn * 0.80, _FlowColorAmt * _FlowAmount);
                    alpha  = saturate(alpha * lerp(1.0, 0.50 + fn * 0.80, _FlowAlphaAmt * _FlowAmount));
                }

                // ---- P3 尾部溶解：判定线侧(t→0)被分形杂色漩涡柔和烧蚀 + 淡淡能量边 ----
                // ★ _DissolveAmount=0 时整段跳过（主游戏默认零影响）
                if (_DissolveAmount > 0.001)
                {
                    float band = ShockDissolveBand(t, _DissolvePoint, _DissolveWidth);
                    float keep, glow;
                    ShockDissolve(float2(vz, vy), band, _DissolveAmount, _DissolveEdge,
                                  _DissolveScale, _DissolveSpeed, _Time.y,
                                  _DissolveStretchX, _DissolveStretchY, _DissolveSwirl * _DissolveAmount,
                                  _DissolveScrollDir.x, _DissolveScrollDir.y,
                                  keep, glow);
                    alpha *= keep;
                    col += _DissolveEdgeColor.rgb * (_DissolveEdgeIntensity * glow);
                }

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
            #include "ShockwaveNoise.hlsl"

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
            half4 _DissolveEdgeColor;
            float  _FlowAmount, _FlowScale, _FlowSpeed, _FlowWarp, _FlowDetail, _FlowAlphaAmt;
            float  _FlowStretchX, _FlowStretchY, _FlowSwirl, _FlowLengthMix;
            float4 _FlowScrollDir;
            float  _DissolveAmount, _DissolvePoint, _DissolveWidth, _DissolveEdge, _DissolveScale;
            float  _DissolveSpeed;
            float  _DissolveStretchX, _DissolveStretchY, _DissolveSwirl;
            float4 _DissolveScrollDir;
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

                // P3 流体：发光也跟着浓淡起伏，才不会"墙身有流动、光却是平的"
                if (_FlowAmount > 0.001)
                {
                    // 与主体 Pass 严格同坐标：x = lerp(vz, 1-t, _FlowLengthMix)，y = vy
                    float2 fp = float2(lerp(IN.vz, 1.0 - IN.t, _FlowLengthMix), IN.vy) * _FlowScale;
                    float fn = ShockFlowNoise(fp, _Time.y, _FlowSpeed,
                                              _FlowWarp * _FlowAmount, _FlowDetail,
                                              _FlowStretchX, _FlowStretchY, _FlowSwirl * _FlowAmount,
                                              _FlowScrollDir.x, _FlowScrollDir.y);
                    mask *= lerp(1.0, 0.50 + fn * 0.80, _FlowAlphaAmt * _FlowAmount);
                }

                // P3 溶解：被烧掉的地方不能再发光（否则会出现"烧没了但光还在"）
                if (_DissolveAmount > 0.001)
                {
                    float band = ShockDissolveBand(IN.t, _DissolvePoint, _DissolveWidth);
                    float keep, glow;
                    ShockDissolve(float2(IN.vz, IN.vy), band, _DissolveAmount, _DissolveEdge,
                                  _DissolveScale, _DissolveSpeed, _Time.y,
                                  _DissolveStretchX, _DissolveStretchY, _DissolveSwirl * _DissolveAmount,
                                  _DissolveScrollDir.x, _DissolveScrollDir.y,
                                  keep, glow);
                    mask *= keep;
                }

                half3 g = _GlowColor.rgb * _GlowIntensity * mask;
                return half4(g, 1.0);
            }
            ENDHLSL
        }
    }
}
