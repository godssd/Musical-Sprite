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
            };

            half4  _ColorDeep, _ColorTip;
            float4 _MainTex_ST;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float2 _ScrollSpeed;
            float  _BackX, _FrontX, _ArchHeight, _FadePower, _GradientPower, _GradientBalance, _EdgeGlow, _Opacity, _Flash;

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS);
                o.uv = IN.uv;

                float span = max(0.0001, abs(_FrontX - _BackX));
                o.t  = clamp((IN.positionOS.x - _BackX) / span, 0.0, 1.0);
                o.vy = clamp(IN.positionOS.y / max(0.0001, _ArchHeight), 0.0, 1.0);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t  = IN.t;
                float vy = IN.vy;

                // 1) alpha 淡入：判定线透明，中缝最浓
                float alpha = pow(t, _FadePower) * _Opacity;

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
    }
}
