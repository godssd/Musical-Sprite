Shader "MusicalSprite/GrassFringe"
{
    Properties
    {
        _EdgeTex("Grass Shape Mask", 2D) = "white" {}
        _RedMap("Red Side Map", 2D) = "white" {}
        _BlueMap("Blue Side Map", 2D) = "white" {}

        _CenterLineX("Center Line X", Float) = 0
        _CenterBlend("Center Blend Width", Range(0, 2)) = 0.15

        [Header(Grass Size)]
        _Height("Tuft Height", Float) = 0.55
        _Outward("Tuft Outward", Float) = 0.35
        _Lift("Lift Above Ground", Float) = 0.02
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.25

        [Header(Shape Variation)]
        _RandomScale("Random Height Range", Range(0, 1)) = 0.35

        [Header(Ground Bounds for Tint)]
        _GroundRenderMin("Render Ground Min", Vector) = (-8, -3.75, 0, 0)
        _GroundRenderMax("Render Ground Max", Vector) = (8, 3.75, 0, 0)

        [Header(Color)]
        _GreenTint("Green Tint", Color) = (0.45, 0.78, 0.18, 1)
        _BrownTint("Brown Tint", Color) = (0.62, 0.48, 0.22, 1)
        _GroundColorAmount("Ground Color Amount", Range(0, 1)) = 0.35
        _TintAmount("Tint Amount", Range(0, 1)) = 0.65
        _ColorVariation("Color Variation", Range(0, 1)) = 0.55
        _Brightness("Brightness", Float) = 1.2
        _RootGroundness("Root Color From Ground", Range(0, 1)) = 0.7

        [Header(Debug)]
        _DebugColor("Debug Tint", Color) = (0.2, 1.0, 0.2, 1)
        _DebugAmount("Debug Amount", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;  // anchor point (root midpoint of this segment)
                float3 tangentOS  : TANGENT;   // path tangent (along the perimeter)
                float3 normalOS   : NORMAL;    // outward horizontal direction of the perimeter
                float3 localPos   : TEXCOORD0; // x: offset along tangent (world units, already includes half-segment width)
                                               // y: 0 = root .. 1 = tip
                float2 uv         : TEXCOORD1; // u: 0..1 across one grass unit, v: 0 = root .. 1 = tip
                float4 color      : COLOR;     // r = height random scale, g = green/brown mix
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 anchorWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  heightNorm : TEXCOORD3;
            };

            TEXTURE2D(_EdgeTex);
            SAMPLER(sampler_EdgeTex);
            TEXTURE2D(_RedMap);
            SAMPLER(sampler_RedMap);
            TEXTURE2D(_BlueMap);
            SAMPLER(sampler_BlueMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _EdgeTex_ST;
                float4 _RedMap_ST;
                float4 _BlueMap_ST;
                float  _CenterLineX;
                float  _CenterBlend;
                float  _Height;
                float  _Outward;
                float  _Lift;
                float  _Cutoff;
                float  _RandomScale;
                float4 _GroundRenderMin;
                float4 _GroundRenderMax;
                float4 _GreenTint;
                float4 _BrownTint;
                float  _GroundColorAmount;
                float  _TintAmount;
                float  _ColorVariation;
                float  _Brightness;
                float  _RootGroundness;
                float4 _DebugColor;
                float  _DebugAmount;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 anchorWS = TransformObjectToWorld(input.positionOS);

                float3 tangentWS = TransformObjectToWorldDir(input.tangentOS);
                tangentWS.y = 0.0;
                tangentWS = normalize(tangentWS);

                float3 outwardWS = TransformObjectToWorldNormal(input.normalOS);
                outwardWS.y = 0.0;
                outwardWS = normalize(outwardWS);

                float3 upWS = float3(0, 1, 0);

                // Per-tuft random height/scale variation only; no yaw/tilt so the
                // grass strip stays aligned with the perimeter (no twisting).
                float randScale = 1.0 + (input.color.r - 0.5) * _RandomScale;
                float h = _Height * randScale;
                float d = _Outward * randScale;

                // Geometry width is fixed by the segment (localPos.x already holds
                // the half-width offset in world units). Height/outward are live params.
                float3 worldPos = anchorWS
                    + upWS * _Lift
                    + tangentWS * input.localPos.x
                    + (outwardWS * d + upWS * h) * input.localPos.y;

                output.positionCS = TransformWorldToHClip(worldPos);
                output.anchorWS = anchorWS;
                output.uv = input.uv;
                output.color = input.color;
                output.heightNorm = input.localPos.y;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 edgeTex = SAMPLE_TEXTURE2D(_EdgeTex, sampler_EdgeTex, input.uv);
                float a = edgeTex.a;

                float effectiveCutoff = lerp(_Cutoff, -0.5, _DebugAmount);
                clip(a - effectiveCutoff);

                // Ground tint at the tuft base for cohesion.
                float2 rmin = _GroundRenderMin.xy;
                float2 rmax = _GroundRenderMax.xy;
                float2 groundUV = saturate((input.anchorWS.xz - rmin) / max(0.0001, (rmax - rmin)));

                float4 redMap  = SAMPLE_TEXTURE2D(_RedMap, sampler_RedMap, groundUV);
                float4 blueMap = SAMPLE_TEXTURE2D(_BlueMap, sampler_BlueMap, groundUV);

                float halfBlend = max(0.0001, _CenterBlend * 0.5);
                float redWeight = 1.0 - smoothstep(_CenterLineX - halfBlend, _CenterLineX + halfBlend, input.anchorWS.x);
                float3 groundTint = lerp(blueMap.rgb, redMap.rgb, redWeight);

                // Per-tuft green/brown mix.
                float colorMix = saturate(input.color.g * 2.0 - 0.5);
                colorMix = lerp(0.45, colorMix, _ColorVariation);
                float3 grassTint = lerp(_BrownTint.rgb, _GreenTint.rgb, colorMix);

                // Blend between ground and grass. The root of the tuft borrows more
                // ground color so it merges with the arena floor; the tip is pure grass.
                float rootness = lerp(1.0, 1.0 - input.heightNorm, _RootGroundness);
                float3 col = lerp(grassTint, groundTint, rootness * _GroundColorAmount);
                col = lerp(col, grassTint, _TintAmount);
                col *= _Brightness;

                col = lerp(col, _DebugColor.rgb, _DebugAmount);

                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float fogCoord = ComputeFogFactor(input.positionCS.z);
                col = MixFog(col, fogCoord);
                #endif

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
