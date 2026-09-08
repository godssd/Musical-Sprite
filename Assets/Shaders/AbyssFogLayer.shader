Shader "MusicalSprite/AbyssFogLayer"
{
    Properties
    {
        // ---------- Core (per layer, set by Setup) ----------
        [Header(Color)]
        _Color("Fog Color", Color) = (0.15, 0.07, 0.22, 1)
        _Alpha("Alpha", Range(0, 1)) = 0.5

        [Header(Edge softness)]
        _EdgeSoftness("Edge Softness", Range(0.01, 2.0)) = 0.8

        [Header(Noise)]
        _NoiseScale("Noise Scale", Float) = 0.12
        _NoiseSpeed("Noise Speed", Float) = 0.08
        _NoiseStrength("Noise Strength", Range(0, 1)) = 0.7

        [Header(Wave)]
        _WaveAmp("Wave Amplitude", Float) = 0.3
        _WaveScale("Wave Scale", Float) = 0.22
        _WaveSpeed("Wave Speed", Float) = 0.5

        [Header(Horizontal fade)]
        _HorizFadeStart("Horiz Fade Start", Float) = 18.0
        _HorizFadeEnd("Horiz Fade End", Float) = 30.0

        // ---------- Bottom (solid) mode ----------
        [Toggle] _IsBottom("Is Bottom (Solid Black)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
            "IgnoreProjector"="True"
        }

        Pass
        {
            Name "AbyssFogLayer"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Alpha;
                float  _EdgeSoftness;
                float  _NoiseScale;
                float  _NoiseSpeed;
                float  _NoiseStrength;
                float  _WaveAmp;
                float  _WaveScale;
                float  _WaveSpeed;
                float  _HorizFadeStart;
                float  _HorizFadeEnd;
                float  _IsBottom;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 worldPos   : TEXCOORD1;
            };

            float2 AbyssHash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453);
            }

            float AbyssNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(dot(AbyssHash2(i + float2(0.0, 0.0)), f - float2(0.0, 0.0)),
                         dot(AbyssHash2(i + float2(1.0, 0.0)), f - float2(1.0, 0.0)), u.x),
                    lerp(dot(AbyssHash2(i + float2(0.0, 1.0)), f - float2(0.0, 1.0)),
                         dot(AbyssHash2(i + float2(1.0, 1.0)), f - float2(1.0, 1.0)), u.x),
                    u.y);
            }

            float AbyssFbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                [unroll(3)]
                for (int i = 0; i < 3; i++)
                {
                    v += a * AbyssNoise(p);
                    p = p * 2.03 + float2(7.3, 3.1);
                    a *= 0.5;
                }
                return v;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 worldPos = TransformObjectToWorld(IN.positionOS.xyz);

                // Vertex wave: the whole layer gently undulates (only for fog layers,
                // not the solid bottom). With many thin layers stacked, this reads as
                // a continuous "rolling" wave like the COTL abyss.
                float wave = sin(worldPos.x * _WaveScale + _Time.y * _WaveSpeed) * _WaveAmp
                           + cos(worldPos.z * _WaveScale * 0.8f + _Time.y * _WaveSpeed * 0.6f) * _WaveAmp * 0.7f;
                worldPos.y += wave * (1.0 - _IsBottom);

                OUT.worldPos   = worldPos;
                OUT.positionHCS = TransformWorldToHClip(worldPos);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 wp = IN.worldPos;

                // Horizontal distance from the arena center; used for the far fade
                // so the fog never abruptly cuts off on screen (no grey gaps).
                float horiz = length(wp.xz);
                float hFade = 1.0 - smoothstep(_HorizFadeStart, _HorizFadeEnd, horiz);
                if (hFade <= 0.0) discard;

                // ---- Bottom (solid black) ----
                if (_IsBottom > 0.5)
                {
                    return half4(_Color.rgb, hFade);
                }

                // ---- Fog layer ----
                // Broken-edge noise: makes the fog read as "cloud puffs" rather than
                // a clean rectangle. Scrolled slowly over time.
                float n = AbyssFbm(wp.xz * _NoiseScale + _Time.y * _NoiseSpeed);
                n = n * 0.5 + 0.5; // -> 0..1
                float edgeNoise = 1.0 - _NoiseStrength + _NoiseStrength * n;

                // Soft edge fade: feathers the alpha as we approach the horizontal
                // fade boundary so there is no hard ring at the fog's outer limit.
                float edgeFade = smoothstep(0.0, _EdgeSoftness, hFade);

                float alpha = _Alpha * edgeNoise * edgeFade;
                if (alpha < 0.003) discard;

                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
