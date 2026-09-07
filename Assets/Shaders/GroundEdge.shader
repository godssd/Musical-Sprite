Shader "MusicalSprite/GroundEdge"
{
    Properties
    {
        _RedMap("Red Side Map", 2D) = "white" {}
        _BlueMap("Blue Side Map", 2D) = "white" {}
        _EdgeTex("Grass Edge Mask", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1, 1, 1, 1)

        _CenterLineX("Center Line X", Float) = 0
        _CenterBlend("Center Blend Width", Range(0, 2)) = 0.15

        [Header(Edge Grass Mask)]
        _EdgeOutset("Edge Outset", Float) = 0.35
        _EdgeTexTiling("Edge Texture Tiling", Float) = 2.0
        _EdgeVerticalScale("Edge Vertical Scale", Float) = 1.0
        _EdgeCutoff("Edge Alpha Cutoff", Range(0, 1)) = 0.25
        _EdgeBrightness("Edge Color Intensity", Float) = 1.05
        _EdgeOverhang("Edge Overhang", Float) = 0.08
        [IntRange] _DebugMode("Debug Mode (0=off, 1=regions, 2=grass alpha heatmap)", Range(0, 2)) = 0

        [Header(Ground Bounds)]
        _GroundMin("Ground Min", Vector) = (-8, -3.75, 0, 0)
        _GroundMax("Ground Max", Vector) = (8, 3.75, 0, 0)
        _CornerRadius("Corner Radius", Float) = 2.0
        _GroundThickness("Ground Thickness", Float) = 0.15

        [Header(Side and Bottom)]
        _SideColor("Side Dirt Color", Color) = (0.45, 0.32, 0.22, 1)
        _BottomColor("Bottom Color", Color) = (0.3, 0.2, 0.15, 1)
        _SideLightDir("Fake Side Light Dir", Vector) = (0.5, 0.3, 0.8, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
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
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1; // x = edgeU (along perimeter), y = edge flag
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float2 edgeData   : TEXCOORD3; // x = edgeU, y = edge flag
            };

            TEXTURE2D(_RedMap);
            SAMPLER(sampler_RedMap);
            TEXTURE2D(_BlueMap);
            SAMPLER(sampler_BlueMap);
            TEXTURE2D(_EdgeTex);
            SAMPLER(sampler_EdgeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _RedMap_ST;
                float4 _BlueMap_ST;
                float4 _EdgeTex_ST;
                float4 _BaseColor;
                float  _CenterLineX;
                float  _CenterBlend;

                float  _EdgeOutset;
                float  _EdgeTexTiling;
                float  _EdgeVerticalScale;
                float  _EdgeCutoff;
                float  _EdgeBrightness;
                float  _EdgeOverhang;
                float  _DebugMode;

                float4 _GroundMin;
                float4 _GroundMax;
                float  _CornerRadius;
                float  _GroundThickness;

                float4 _SideColor;
                float4 _BottomColor;
                float4 _SideLightDir;
            CBUFFER_END

            // Signed-distance function for a rounded rectangle in the XZ plane.
            // The mesh uses OUTWARD rounded corners (the arc bulges outside the rectangle).
            // For that shape, the SDF is: length(max(abs(p)-halfSize+r, 0)) - r.
            // Returns distance from p to the inner boundary (negative inside, positive outside).
            float RoundedRectSDF(float2 p, float2 bmin, float2 bmax, float r)
            {
                float2 center = (bmin + bmax) * 0.5;
                float2 halfSize = (bmax - bmin) * 0.5;
                float2 d = abs(p - center) - halfSize + r;
                return length(max(d, 0.0)) - r;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                // ------------------------------------------------------------------
                // The mesh already contains the outward grass fringe as a separate
                // edge-ring (see GroundEdgeMaterialSetup.cs). We do NOT push vertices
                // here anymore; _EdgeOutset is only used by the fragment shader to
                // know how far the fringe extends and to sample the grass mask.
                // ------------------------------------------------------------------

                output.positionCS = TransformWorldToHClip(posWS);
                output.positionWS = posWS;
                output.normalWS = normalWS;
                output.uv = input.uv;
                output.edgeData = input.uv2;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float3 worldPos = input.positionWS;
                float3 normalWS = normalize(input.normalWS);

                // ------------------------------------------------------------------
                // Side / bottom faces: render as dirt. Detect by uv2.y (set to 2 in the mesh).
                // We do not use world-space normal here because the thin side wall normals
                // were being averaged with the top face by RecalculateNormals().
                // ------------------------------------------------------------------
                if (input.edgeData.y > 1.5)
                {
                    if (_DebugMode > 0.5)
                        return float4(0.0, 0.0, 1.0, 1.0);

                    // Bottom cap faces downward; cull it.
                    if (normalWS.y < -0.3)
                        clip(-1.0);

                    float3 lightDir = normalize(_SideLightDir.xyz);
                    float NdotL = saturate(dot(normalWS, lightDir));
                    float light = lerp(0.65, 1.0, NdotL);
                    float3 col = _SideColor.rgb * light;

                    #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    float fogCoord = ComputeFogFactor(input.positionCS.z);
                    col = MixFog(col, fogCoord);
                    #endif

                    return float4(col, 1.0);
                }

                // ------------------------------------------------------------------
                // Top face: red/blue ground texture blended by the center line.
                // The edge fringe uses the SAME ground colour; _EdgeTex only cuts the shape.
                // ------------------------------------------------------------------
                float2 worldXZ = worldPos.xz;
                float2 rmin = _GroundMin.xy;
                float2 rmax = _GroundMax.xy;

                // Ground UV (clamp so the fringe repeats the border colour rather than tiling).
                float u = saturate((worldXZ.x - rmin.x) / max(0.0001, (rmax.x - rmin.x)));
                float v = saturate((worldXZ.y - rmin.y) / max(0.0001, (rmax.y - rmin.y)));
                float2 groundUV = float2(u, v);

                float4 redMap  = SAMPLE_TEXTURE2D(_RedMap, sampler_RedMap, groundUV);
                float4 blueMap = SAMPLE_TEXTURE2D(_BlueMap, sampler_BlueMap, groundUV);

                float halfBlend = max(0.0001, _CenterBlend * 0.5);
                float redWeight = 1.0 - smoothstep(_CenterLineX - halfBlend, _CenterLineX + halfBlend, worldXZ.x);
                float3 groundCol = lerp(blueMap.rgb, redMap.rgb, redWeight) * _BaseColor.rgb;

                // Distance from the original inner boundary (negative inside, positive outside).
                float edgeDist = RoundedRectSDF(worldXZ, rmin, rmax, _CornerRadius);

                float3 finalCol = groundCol;

                // Grass rim = the outermost strip of the REAL ground.
                // edgeDist = 0 at the real edge, edgeDist = -_EdgeOutset at the rim's inner edge.
                if (edgeDist > -_EdgeOutset)
                {
                    if (_DebugMode > 0.5 && _DebugMode < 1.5)
                        return float4(0.0, 1.0, 0.0, 1.0);

                    // Safety: never render anything beyond the overhang edge.
                    if (edgeDist > _EdgeOverhang)
                        clip(-1.0);

                    // t: 0 at the rim's inner edge (meets the red/blue field),
                    //    1 at the outer edge (grass tips point outward, possibly overhanging).
                    float t = (edgeDist + _EdgeOutset) / max(0.0001, _EdgeOutset + _EdgeOverhang);
                    float edgeU = input.edgeData.x;
                    float edgeV = saturate(t * _EdgeVerticalScale);
                    float2 edgeUV = float2(edgeU * _EdgeTexTiling, edgeV);

                    float4 edgeTex = SAMPLE_TEXTURE2D(_EdgeTex, sampler_EdgeTex, edgeUV);
                    float edgeAlpha = edgeTex.a;

                    if (_DebugMode > 1.5)
                    {
                        // Heatmap: black = alpha 0, green = alpha 1, so you can see
                        // whether the grass mask is being sampled at all.
                        float3 heat = lerp(float3(0.0, 0.0, 0.0), float3(0.0, 1.0, 0.0), edgeAlpha);
                        return float4(heat, 1.0);
                    }

                    // No grass right on the center seam. The grass mask should be 0 at the
                    // center line and fully present everywhere else along the rim.
                    float seamDist = abs(worldXZ.x - _CenterLineX);
                    float seamMask = smoothstep(0.0, _CenterBlend, seamDist);
                    edgeAlpha *= seamMask;

                    // Grass mask: alpha = grass, alpha = 0 = no grass (the gaps the
                    // texture did not paint). The top face was intentionally overhung by
                    // _EdgeOverhang so that clipping these gaps reveals the dirt side wall
                    // underneath, never open sky — the area does NOT shrink.
                    float mask = smoothstep(_EdgeCutoff, _EdgeCutoff + 0.08, edgeAlpha);
                    if (mask < 0.5)
                        clip(-1.0);

                    // Where the grass silhouette is present, keep the red/blue field.
                    finalCol = groundCol;
                }
                else
                {
                    if (_DebugMode > 0.5 && _DebugMode < 1.5)
                        return float4(1.0, 0.0, 0.0, 1.0);
                }

                // Simple directional fake light for the top.
                float3 lightDir = normalize(float3(0.5, 1.0, 0.3));
                float NdotL = saturate(dot(normalWS, lightDir));
                float light = lerp(0.7, 1.0, NdotL);
                finalCol *= light;

                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float fogCoord = ComputeFogFactor(input.positionCS.z);
                finalCol = MixFog(finalCol, fogCoord);
                #endif

                return float4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
}
