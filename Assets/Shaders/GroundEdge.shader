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
        _EdgeOverhang("Edge Overhang", Float) = 0.08
        [IntRange] _DebugMode("Debug Mode (0=off, 1=regions, 2=grass alpha heatmap)", Range(0, 2)) = 0

        [Header(Stage Disc Mode)]
        _ShapeMode("Shape Mode (0=RoundedRect, 1=Disc)", Range(0, 1)) = 0
        _StageMap("Stage Map (Disc Mode)", 2D) = "white" {}
        _DiscCenter("Disc Center (XZ)", Vector) = (0, 0, 0, 0)
        _DiscRadius("Disc Radius", Float) = 1.2
        // 圆盘顶面在世界空间的 Y 高度（台面网格 y∈[0,0.15] × localScale.y）。
        // 侧壁判定用：低于它 = 侧壁。网格侧壁法线被共用顶点平均坏了，不能信。
        _DiscTopY("Disc Top World Y", Float) = 0.6

        [Header(Grass Outline)]
        _OutlineColor("Grass Outline Color", Color) = (0.05, 0.05, 0.08, 1)
        // 描边线宽（世界单位）：在草沿贴图上按此宽度换算纹素邻域，只染线本身。
        _EdgeOutlineWidth("Grass Outline Width (world units)", Float) = 0.02

        [Header(Stage Footprint Clip (Rect Ground Only))]
        // 矩形主地面在台面圆盘占位范围内直接裁掉：台面压在主地面边界上，
        // 不裁的话地面草沿带会从台面直边侧壁下面探出来形成“裙边”。
        // xy=圆心XZ, z=裁剪半径(台面半径+微量), w=开关
        _StageClipA("Stage Clip A (x, z, radius, on)", Vector) = (8, 0, 1.2005, 0)
        _StageClipB("Stage Clip B (x, z, radius, on)", Vector) = (-8, 0, 1.2005, 0)

        [Header(Ground Bounds)]
        _GroundMin("Ground Min", Vector) = (-8, -3.75, 0, 0)
        _GroundMax("Ground Max", Vector) = (8, 3.75, 0, 0)
        _CornerRadius("Corner Radius", Float) = 2.0
        _GroundThickness("Ground Thickness", Float) = 0.15

        [Header(Side and Bottom)]
        _SideColor("Side Dirt Color", Color) = (0.45, 0.32, 0.22, 1)
        _SideLightDir("Fake Side Light Dir", Vector) = (0.5, 0.3, 0.8, 0)

        [Header(Ground Shadow)]
        _ShadowColor("Shadow Tint", Color) = (0.35, 0.32, 0.45, 1)
        _ShadowIntensity("Shadow Intensity", Range(0, 1)) = 0.55
        _ShadowSoftness("Shadow Softness", Range(0, 1)) = 0.35
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
            TEXTURE2D(_StageMap);
            SAMPLER(sampler_StageMap);

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
                float  _EdgeOverhang;
                float  _DebugMode;

                float4 _GroundMin;
                float4 _GroundMax;
                float  _CornerRadius;
                float  _GroundThickness;

                float4 _SideColor;
                float4 _SideLightDir;
                float4 _ShadowColor;
                float  _ShadowIntensity;
                float  _ShadowSoftness;

                float4 _StageMap_ST;
                float  _ShapeMode;
                float4 _DiscCenter;
                float  _DiscRadius;
                float4 _OutlineColor;
                float  _EdgeOutlineWidth;
                float4 _EdgeTex_TexelSize;
                float4 _StageClipA;
                float4 _StageClipB;
                float  _DiscTopY;
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
                // Side / bottom faces: render as dirt. Detect by uv2.y (set to 2 in the
                // generated ground mesh), or — in disc mode (stage platforms) — by world
                // height below _DiscTopY. The disc mesh shares vertices between the top
                // cap and the side skirt, so RecalculateNormals-style averaging left the
                // skirt normals pointing almost straight up/down (|ny| ~ 0.98); a normal
                // test alone can never catch them.
                // ------------------------------------------------------------------
                bool isSideFace = input.edgeData.y > 1.5
                    || (_ShapeMode > 0.5 && (normalWS.y < 0.3 || worldPos.y < _DiscTopY - 0.002));
                if (isSideFace)
                {
                    if (_DebugMode > 0.5)
                        return float4(0.0, 0.0, 1.0, 1.0);

                    // Bottom cap faces downward; cull it.
                    if (normalWS.y < -0.3)
                        clip(-1.0);

                // 圆盘侧壁：网格法线已被平均破坏，改用从圆心出发的径向水平假法线，
                // 保证侧壁光照方向一致、呈现干净的泥土色。
                float3 sideNormal = normalWS;
                if (_ShapeMode > 0.5)
                {
                    float2 radial = worldPos.xz - _DiscCenter.xy;
                    sideNormal = normalize(float3(radial.x, 0.0, radial.y));
                }

                float3 lightDir = normalize(_SideLightDir.xyz);
                float NdotL = saturate(dot(sideNormal, lightDir));
                float light = lerp(0.65, 1.0, NdotL);
                float3 col = _SideColor.rgb * light;

                // 侧面接受同样的卡通化着色阴影，保持与顶面一致
                float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
                float shadowAtten = MainLightRealtimeShadow(shadowCoord);
                float softShadow = smoothstep(0.0, _ShadowSoftness, shadowAtten);
                float3 shadowedCol = col * lerp(float3(1.0, 1.0, 1.0), _ShadowColor.rgb, _ShadowIntensity);
                col = lerp(shadowedCol, col, softShadow);

                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float fogCoord = ComputeFogFactor(input.positionCS.z);
                col = MixFog(col, fogCoord);
                #endif

                return float4(col, 1.0);
                }

                // ------------------------------------------------------------------
                // Top face: red/blue ground texture blended by the center line.
                //
                // IMPORTANT: this shader is a NEUTRAL CANVAS. It outputs the ground
                // albedo faithfully and performs NO ambient / focus / contact-AO /
                // mottle math. All global atmosphere (sunny colour grade, vignette,
                // dynamic light pool, contact shadow) is applied by the post-processing
                // Volume (SampleSceneProfile) and by global lights. This keeps the look
                // consistent across every ground-texture variant without restricting the
                // art, and removes the hardcoded "black line" / "overcast" band entirely.
                // ------------------------------------------------------------------
                float2 worldXZ = worldPos.xz;

                // 矩形主地面：台面圆盘占位范围内直接裁掉（含少量余量盖住外挑草尖），
                // 防止地面草沿带从台面直边侧壁下面探出来。只对矩形模式生效，
                // 圆盘模式是台面本体，绝不能裁。
                if (_ShapeMode < 0.5)
                {
                    if (_StageClipA.w > 0.5 && distance(worldXZ, _StageClipA.xy) < _StageClipA.z)
                        clip(-1.0);
                    if (_StageClipB.w > 0.5 && distance(worldXZ, _StageClipB.xy) < _StageClipB.z)
                        clip(-1.0);
                }

                float2 rmin = _GroundMin.xy;
                float2 rmax = _GroundMax.xy;

                float3 groundCol;
                float edgeDist;

                if (_ShapeMode > 0.5)
                {
                    // ------------------------------------------------------------------
                    // Disc mode (stage platforms): texture mapped across the disc,
                    // signed distance = distance to the disc boundary (negative inside).
                    // ------------------------------------------------------------------
                    edgeDist = distance(worldXZ, _DiscCenter.xy) - _DiscRadius;
                    float2 stageUV = (worldXZ - _DiscCenter.xy) / max(0.0001, 2.0 * _DiscRadius) + 0.5;
                    float4 stageTex = SAMPLE_TEXTURE2D(_StageMap, sampler_StageMap, stageUV);
                    // 透明像素用原图 RGB 会透出蓝青色（yuantai 贴图透明区存色），
                    // 圆盘顶面应只在贴图不透明处显示图案，透明处露泥土棕。
                    groundCol = lerp(_SideColor.rgb, stageTex.rgb * _BaseColor.rgb, stageTex.a);
                }
                else
                {
                    // Ground UV (clamp so the fringe repeats the border colour rather than tiling).
                    float u = saturate((worldXZ.x - rmin.x) / max(0.0001, (rmax.x - rmin.x)));
                    float v = saturate((worldXZ.y - rmin.y) / max(0.0001, (rmax.y - rmin.y)));
                    float2 groundUV = float2(u, v);

                    float4 redMap  = SAMPLE_TEXTURE2D(_RedMap, sampler_RedMap, groundUV);
                    float4 blueMap = SAMPLE_TEXTURE2D(_BlueMap, sampler_BlueMap, groundUV);

                    float halfBlend = max(0.0001, _CenterBlend * 0.5);
                    float redWeight = 1.0 - smoothstep(_CenterLineX - halfBlend, _CenterLineX + halfBlend, worldXZ.x);
                    groundCol = lerp(blueMap.rgb, redMap.rgb, redWeight) * _BaseColor.rgb;

                    // Distance from the original inner boundary (negative inside, positive outside).
                    edgeDist = RoundedRectSDF(worldXZ, rmin, rmax, _CornerRadius);
                }

                float3 finalCol = groundCol;

                // Grass rim = the outermost strip of the REAL ground.
                // Rect ground: edgeDist = 0 at the real edge, -_EdgeOutset at the rim's inner
                // edge, +_EdgeOverhang at the fringe tips (fringe geometry overhangs).
                // Disc stage: the mesh ends at the boundary, so the grass band lives fully
                // inside (edgeDist from -_EdgeOutset up to 0).
                if (edgeDist > -_EdgeOutset)
                {
                    // 矩形地面草沿带：如果伸进台面圆盘占位区域内，同样裁掉。
                    // 否则草沿带会和台面侧壁/顶面重叠，在交界处形成竖线/杂色。
                    // 草沿带内边界比顶面内缩了 _EdgeOutset，所以裁剪半径需要按勾股定理
                    // 加长：sqrt(r^2 + EdgeOutset^2)，否则圆盘端点处会露出一小段。
                    if (_ShapeMode < 0.5)
                    {
                        float grassClipA = sqrt(_StageClipA.z * _StageClipA.z + _EdgeOutset * _EdgeOutset);
                        float grassClipB = sqrt(_StageClipB.z * _StageClipB.z + _EdgeOutset * _EdgeOutset);
                        if (_StageClipA.w > 0.5 && distance(worldXZ, _StageClipA.xy) < grassClipA) clip(-1.0);
                        if (_StageClipB.w > 0.5 && distance(worldXZ, _StageClipB.xy) < grassClipB) clip(-1.0);
                    }

                    if (_DebugMode > 0.5 && _DebugMode < 1.5)
                        return float4(0.0, 1.0, 0.0, 1.0);

                    // Safety: never render anything beyond the overhang edge.
                    if (edgeDist > _EdgeOverhang)
                        clip(-1.0);

                    // t: 0 at the rim's inner edge (meets the red/blue field),
                    //    1 at the outer edge (grass tips point outward, possibly overhanging).
                    float t = (edgeDist + _EdgeOutset) / max(0.0001, _EdgeOutset + _EdgeOverhang);
                    // Perimeter U: rect ground uses the mesh's uv2.x ring; disc stage mesh
                    // has no ring, so derive it from the world angle around the disc.
                    float edgeU = input.edgeData.x;
                    if (_ShapeMode > 0.5)
                        edgeU = atan2(worldXZ.y - _DiscCenter.y, worldXZ.x - _DiscCenter.x) * 0.15915494 + 0.5;
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

                    // Grass mask: alpha = grass, alpha = 0 = no grass (the gaps the
                    // texture did not paint). The top face was intentionally overhung by
                    // _EdgeOverhang so that clipping these gaps reveals the dirt side wall
                    // underneath, never open sky — the area does NOT shrink.
                    float mask = smoothstep(_EdgeCutoff, _EdgeCutoff + 0.08, edgeAlpha);

                    // 描边 = V 向（带宽方向）单向检测，只画在草像素内侧：
                    // 草像素沿 V 往外一步没草 → 轮廓线。线完全贴着草形轮廓内侧，
                    // 不越出几何边界——旧"外半段"画在悬挑区，会被 overhang 裁剪
                    // 截出平直断口（圆台锯齿尖的"截断"感），还会给接缝杂点包边。
                    // 线宽 = kTex 纹素对应的世界宽度，由 _EdgeOutlineWidth 控制。
                    bool grassHere = mask >= 0.5;
                    float bandDepth = max(0.0001, _EdgeOutset + _EdgeOverhang);
                    float worldPerVTexel = bandDepth * _EdgeTex_TexelSize.y / max(0.0001, _EdgeVerticalScale);
                    float kTex = clamp(_EdgeOutlineWidth / max(0.0001, worldPerVTexel), 1.0, 256.0);
                    // V 向采样 clamp 到 [0,1]：贴图 Repeat 寻址下带内缘(t≈0)向上采样
                    // 会绕到贴图顶部（草尖端 α≈0）画出假线；U 向周向平铺保持 Repeat。
                    float vHi = clamp(edgeUV.y + kTex * _EdgeTex_TexelSize.y, 0.0, 1.0);
                    float aOut = SAMPLE_TEXTURE2D(_EdgeTex, sampler_EdgeTex, float2(edgeUV.x, vHi)).a;
                    float lvl = _EdgeCutoff + 0.04;
                    bool onLine = grassHere && aOut < lvl;

                    if (onLine)
                        finalCol = _OutlineColor.rgb;
                    else if (grassHere)
                        finalCol = groundCol;
                    else if (_ShapeMode > 0.5)
                        finalCol = _SideColor.rgb;      // 圆盘草洞填泥土棕
                    else
                        clip(-1.0);                     // 矩形草洞保持镂空
                }

                // 接收主光源实时阴影（轮廓真实、随光源角度变化），但用卡通化着色/柔化加工，
                // 避免死黑硬边：阴影区 = 原色染上 _ShadowColor 并压暗 _ShadowIntensity，
                // 边缘用 smoothstep 柔化过渡。这是 COTL 式柔和实时投影的核心。
                // 若未来地面出现过多细碎阴影，可调 Directional Light 的 Shadow Distance / Bias，
                // 或关闭非角色物体的 Cast Shadows。
                float4 shadowCoord = TransformWorldToShadowCoord(worldPos);
                float shadowAtten = MainLightRealtimeShadow(shadowCoord);
                float softShadow = smoothstep(0.0, _ShadowSoftness, shadowAtten);
                float3 shadowedCol = finalCol * lerp(float3(1.0, 1.0, 1.0), _ShadowColor.rgb, _ShadowIntensity);
                finalCol = lerp(shadowedCol, finalCol, softShadow);

                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                float fogCoord = ComputeFogFactor(input.positionCS.z);
                finalCol = MixFog(finalCol, fogCoord);
                #endif

                return float4(finalCol, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DepthOnly pass: writes the platform into the camera depth texture when
        // a plain DEPTH prepass runs (LightMode = "DepthOnly"). Mirrors the
        // ForwardLit silhouette (grass-rim / side-wall clip) so the depth outline
        // matches what is actually drawn. Harmless on PC (SSAO uses DepthNormals),
        // but required on paths that rely on the depth prepass (e.g. mobile).
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthOnlyAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1; // x = edgeU (along perimeter), y = edge flag
            };

            struct DepthOnlyVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 edgeData   : TEXCOORD3; // x = edgeU, y = edge flag
            };

            TEXTURE2D(_EdgeTex);
            SAMPLER(sampler_EdgeTex);

            // NOTE: This CBUFFER must mirror the ForwardLit pass EXACTLY (same members,
            // same order) so the SRP batcher sees one consistent UnityPerMaterial layout.
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
                float  _EdgeOverhang;
                float  _DebugMode;
                float4 _GroundMin;
                float4 _GroundMax;
                float  _CornerRadius;
                float  _GroundThickness;
                float4 _SideColor;
                float4 _SideLightDir;
                float4 _ShadowColor;
                float  _ShadowIntensity;
                float  _ShadowSoftness;

                float4 _StageMap_ST;
                float  _ShapeMode;
                float4 _DiscCenter;
                float  _DiscRadius;
                float4 _OutlineColor;
                float  _EdgeOutlineWidth;
                float4 _EdgeTex_TexelSize;
                float4 _StageClipA;
                float4 _StageClipB;
                float  _DiscTopY;
            CBUFFER_END

            // Signed-distance function for a rounded rectangle in the XZ plane.
            float RoundedRectSDF(float2 p, float2 bmin, float2 bmax, float r)
            {
                float2 center = (bmin + bmax) * 0.5;
                float2 halfSize = (bmax - bmin) * 0.5;
                float2 d = abs(p - center) - halfSize + r;
                return length(max(d, 0.0)) - r;
            }

            // Discards exactly the fragments the ForwardLit pass discards, so the
            // depth silhouette matches the visible grass-rim silhouette.
            void ClipGroundEdge(float3 worldPos, float3 normalWS, float2 edgeData)
            {
                bool isSideFace = edgeData.y > 1.5
                    || (_ShapeMode > 0.5 && (normalWS.y < 0.3 || worldPos.y < _DiscTopY - 0.002));
                if (isSideFace)
                {
                    // Bottom cap faces downward; cull it (matches ForwardLit).
                    if (normalWS.y < -0.3)
                        clip(-1.0);
                    return;
                }

                float2 worldXZ = worldPos.xz;
                // 矩形主地面：台面占位裁剪（与 ForwardLit 镜像，保证深度轮廓一致）。
                if (_ShapeMode < 0.5)
                {
                    if (_StageClipA.w > 0.5 && distance(worldXZ, _StageClipA.xy) < _StageClipA.z) clip(-1.0);
                    if (_StageClipB.w > 0.5 && distance(worldXZ, _StageClipB.xy) < _StageClipB.z) clip(-1.0);
                }
                float edgeDist = (_ShapeMode > 0.5)
                    ? distance(worldXZ, _DiscCenter.xy) - _DiscRadius
                    : RoundedRectSDF(worldXZ, _GroundMin.xy, _GroundMax.xy, _CornerRadius);

                if (edgeDist > -_EdgeOutset)
                {
                    // 矩形地面草沿带：同样按 sqrt(r^2 + EdgeOutset^2) 裁剪，避免端点残留。
                    if (_ShapeMode < 0.5)
                    {
                        float grassClipA = sqrt(_StageClipA.z * _StageClipA.z + _EdgeOutset * _EdgeOutset);
                        float grassClipB = sqrt(_StageClipB.z * _StageClipB.z + _EdgeOutset * _EdgeOutset);
                        if (_StageClipA.w > 0.5 && distance(worldXZ, _StageClipA.xy) < grassClipA) clip(-1.0);
                        if (_StageClipB.w > 0.5 && distance(worldXZ, _StageClipB.xy) < grassClipB) clip(-1.0);
                    }

                    // Safety: never write depth beyond the overhang edge.
                    if (edgeDist > _EdgeOverhang)
                        clip(-1.0);

                    float t = (edgeDist + _EdgeOutset) / max(1e-4, _EdgeOutset + _EdgeOverhang);
                    float edgeU = edgeData.x;
                    if (_ShapeMode > 0.5)
                        edgeU = atan2(worldXZ.y - _DiscCenter.y, worldXZ.x - _DiscCenter.x) * 0.15915494 + 0.5;
                    float2 edgeUV = float2(edgeU * _EdgeTexTiling, saturate(t * _EdgeVerticalScale));
                    float edgeAlpha = SAMPLE_TEXTURE2D(_EdgeTex, sampler_EdgeTex, edgeUV).a;

                    float mask = smoothstep(_EdgeCutoff, _EdgeCutoff + 0.08, edgeAlpha);
                    // 镜像 ForwardLit：只有“无草、且向草内探一小步也无草”的矩形片段
                    // 才裁剪（描边带 / 圆盘草洞棕色填充都保留深度）。
                    bool grassHere = mask >= 0.5;
                    // 镜像 ForwardLit：描边只画在草内侧（保留深度），无草矩形片段
                    // 直接裁剪；圆盘草洞保留（ForwardLit 填棕）。
                    if (!grassHere && _ShapeMode < 0.5)
                        clip(-1.0);
                }
            }

            DepthOnlyVaryings DepthOnlyVert(DepthOnlyAttributes input)
            {
                DepthOnlyVaryings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(posWS);
                output.positionWS = posWS;
                output.normalWS = normalWS;
                output.edgeData = input.uv2;
                return output;
            }

            half DepthOnlyFrag(DepthOnlyVaryings input) : SV_TARGET
            {
                ClipGroundEdge(input.positionWS, normalize(input.normalWS), input.edgeData);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // DepthNormals pass: writes the platform into BOTH the camera depth
        // texture and the camera normals texture. This is the pass the SSAO
        // depth-normal prepass (enabled on PC) actually renders. Without it the
        // platform was missing from _CameraDepthTexture, so the fog sampled the
        // background depth behind the platform and the whole slab looked wrongly
        // fogged. Clip logic mirrors ForwardLit so the silhouette matches.
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1; // x = edgeU (along perimeter), y = edge flag
            };

            struct DepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 edgeData   : TEXCOORD3; // x = edgeU, y = edge flag
            };

            TEXTURE2D(_EdgeTex);
            SAMPLER(sampler_EdgeTex);

            // NOTE: This CBUFFER must mirror the ForwardLit pass EXACTLY (same members,
            // same order) so the SRP batcher sees one consistent UnityPerMaterial layout.
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
                float  _EdgeOverhang;
                float  _DebugMode;
                float4 _GroundMin;
                float4 _GroundMax;
                float  _CornerRadius;
                float  _GroundThickness;
                float4 _SideColor;
                float4 _SideLightDir;
                float4 _ShadowColor;
                float  _ShadowIntensity;
                float  _ShadowSoftness;

                float4 _StageMap_ST;
                float  _ShapeMode;
                float4 _DiscCenter;
                float  _DiscRadius;
                float4 _OutlineColor;
                float  _EdgeOutlineWidth;
                float4 _EdgeTex_TexelSize;
                float4 _StageClipA;
                float4 _StageClipB;
                float  _DiscTopY;
            CBUFFER_END

            float RoundedRectSDF(float2 p, float2 bmin, float2 bmax, float r)
            {
                float2 center = (bmin + bmax) * 0.5;
                float2 halfSize = (bmax - bmin) * 0.5;
                float2 d = abs(p - center) - halfSize + r;
                return length(max(d, 0.0)) - r;
            }

            void ClipGroundEdge(float3 worldPos, float3 normalWS, float2 edgeData)
            {
                bool isSideFace = edgeData.y > 1.5
                    || (_ShapeMode > 0.5 && (normalWS.y < 0.3 || worldPos.y < _DiscTopY - 0.002));
                if (isSideFace)
                {
                    if (normalWS.y < -0.3)
                        clip(-1.0);
                    return;
                }

                float2 worldXZ = worldPos.xz;
                // 矩形主地面：台面占位裁剪（与 ForwardLit 镜像，保证深度轮廓一致）。
                if (_ShapeMode < 0.5)
                {
                    if (_StageClipA.w > 0.5 && distance(worldXZ, _StageClipA.xy) < _StageClipA.z) clip(-1.0);
                    if (_StageClipB.w > 0.5 && distance(worldXZ, _StageClipB.xy) < _StageClipB.z) clip(-1.0);
                }
                float edgeDist = (_ShapeMode > 0.5)
                    ? distance(worldXZ, _DiscCenter.xy) - _DiscRadius
                    : RoundedRectSDF(worldXZ, _GroundMin.xy, _GroundMax.xy, _CornerRadius);

                if (edgeDist > -_EdgeOutset)
                {
                    // 矩形地面草沿带：同样按 sqrt(r^2 + EdgeOutset^2) 裁剪，避免端点残留。
                    if (_ShapeMode < 0.5)
                    {
                        float grassClipA = sqrt(_StageClipA.z * _StageClipA.z + _EdgeOutset * _EdgeOutset);
                        float grassClipB = sqrt(_StageClipB.z * _StageClipB.z + _EdgeOutset * _EdgeOutset);
                        if (_StageClipA.w > 0.5 && distance(worldXZ, _StageClipA.xy) < grassClipA) clip(-1.0);
                        if (_StageClipB.w > 0.5 && distance(worldXZ, _StageClipB.xy) < grassClipB) clip(-1.0);
                    }

                    if (edgeDist > _EdgeOverhang)
                        clip(-1.0);

                    float t = (edgeDist + _EdgeOutset) / max(1e-4, _EdgeOutset + _EdgeOverhang);
                    float edgeU = edgeData.x;
                    if (_ShapeMode > 0.5)
                        edgeU = atan2(worldXZ.y - _DiscCenter.y, worldXZ.x - _DiscCenter.x) * 0.15915494 + 0.5;
                    float2 edgeUV = float2(edgeU * _EdgeTexTiling, saturate(t * _EdgeVerticalScale));
                    float edgeAlpha = SAMPLE_TEXTURE2D(_EdgeTex, sampler_EdgeTex, edgeUV).a;

                    float mask = smoothstep(_EdgeCutoff, _EdgeCutoff + 0.08, edgeAlpha);
                    // 镜像 ForwardLit：只有“无草、且向草内探一小步也无草”的矩形片段
                    // 才裁剪（描边带 / 圆盘草洞棕色填充都保留深度）。
                    bool grassHere = mask >= 0.5;
                    // 镜像 ForwardLit：描边只画在草内侧（保留深度），无草矩形片段
                    // 直接裁剪；圆盘草洞保留（ForwardLit 填棕）。
                    if (!grassHere && _ShapeMode < 0.5)
                        clip(-1.0);
                }
            }

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(posWS);
                output.positionWS = posWS;
                output.normalWS = normalWS;
                output.edgeData = input.uv2;
                return output;
            }

            void DepthNormalsFrag(DepthNormalsVaryings input, out half4 outNormalWS : SV_Target0)
            {
                ClipGroundEdge(input.positionWS, normalize(input.normalWS), input.edgeData);
                float3 normalWS = normalize(input.normalWS);
                outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}
