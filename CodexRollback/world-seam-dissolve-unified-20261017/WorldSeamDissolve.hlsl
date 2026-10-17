#ifndef MUSICAL_SPRITE_WORLD_SEAM_DISSOLVE_INCLUDED
#define MUSICAL_SPRITE_WORLD_SEAM_DISSOLVE_INCLUDED

// Shared world-space reveal for the arena and all A/B scene props. The mask is
// authored offline with VFX Texture Lab; movement comes only from _WorldSeamX.
TEXTURE2D(_WorldSeamDissolveTex);
SAMPLER(sampler_WorldSeamDissolveTex);
float _WorldSeamX;
float _WorldSeamMaskScale;
float _WorldSeamNoiseWidth;
float _WorldSeamFeather;
float _WorldSeamEdgeWidth;
float _WorldSeamEdgeStrength;
float4 _WorldSeamEdgeColorA;
float4 _WorldSeamEdgeColorB;
float _WorldSeamPropMaskScale;
float _WorldSeamPropNoiseWidth;
float _WorldSeamPropEdgeWidth;
float _WorldSeamPropEdgeStrength;

float WorldSeamDistance(float3 worldPos)
{
    // On upright sprites, lift the second coordinate gently with height. Their
    // roots still meet the ground contour instead of fading in as whole sprites.
    float2 uv = float2(worldPos.x, worldPos.z + max(worldPos.y, 0.0) * 0.20)
                * _WorldSeamMaskScale;
    float broad = SAMPLE_TEXTURE2D(_WorldSeamDissolveTex,
                                   sampler_WorldSeamDissolveTex, uv).r;
    float fine = SAMPLE_TEXTURE2D(_WorldSeamDissolveTex,
                                  sampler_WorldSeamDissolveTex,
                                  uv * 2.37 + float2(0.29, 0.61)).r;
    float mask = broad * 0.7 + fine * 0.3;
    return worldPos.x - _WorldSeamX + (mask - 0.5) * _WorldSeamNoiseWidth;
}

float WorldSeamRedWeight(float distanceToSeam)
{
    float feather = max(_WorldSeamFeather, 0.001);
    return 1.0 - smoothstep(-feather, feather, distanceToSeam);
}

float WorldSeamPropDistance(float3 worldPos, float3 objectAnchor)
{
    // Keep the shared ground contour, then add finer marks in each sprite's
    // own vertical plane. The extra term changes only the dissolve edge.
    float baseDistance = WorldSeamDistance(float3(worldPos.x, 0.0, objectAnchor.z));
    float2 seed = frac(objectAnchor.xz * float2(0.173, 0.317)
                       + objectAnchor.y * float2(0.11, 0.23));
    float2 localUV = (worldPos.xy - objectAnchor.xy) * _WorldSeamPropMaskScale + seed;
    float broad = SAMPLE_TEXTURE2D(_WorldSeamDissolveTex,
                                   sampler_WorldSeamDissolveTex, localUV).r;
    float fine = SAMPLE_TEXTURE2D(_WorldSeamDissolveTex,
                                  sampler_WorldSeamDissolveTex,
                                  localUV * 2.13 + float2(0.41, 0.17)).r;
    float localMask = broad * 0.65 + fine * 0.35;
    return baseDistance + (localMask - 0.5) * _WorldSeamPropNoiseWidth;
}

float WorldSeamPropEdge(float distanceToSeam)
{
    return (1.0 - smoothstep(0.0, max(_WorldSeamPropEdgeWidth, 0.001),
                              abs(distanceToSeam))) * _WorldSeamPropEdgeStrength;
}

float WorldSeamEdge(float distanceToSeam)
{
    return (1.0 - smoothstep(0.0, max(_WorldSeamEdgeWidth, 0.001),
                              abs(distanceToSeam))) * _WorldSeamEdgeStrength;
}

#endif
