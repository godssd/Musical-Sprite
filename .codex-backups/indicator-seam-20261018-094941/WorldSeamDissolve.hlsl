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

float WorldSeamDistance(float3 worldPos)
{
    // On upright sprites, lift the second coordinate gently with height. Their
    // roots still meet the ground contour instead of fading in as whole sprites.
    float2 uv = float2(worldPos.x, worldPos.z + max(worldPos.y, 0.0) * 0.20)
                * _WorldSeamMaskScale;
    float mask = SAMPLE_TEXTURE2D(_WorldSeamDissolveTex,
                                  sampler_WorldSeamDissolveTex, uv).r;
    return worldPos.x - _WorldSeamX + (mask - 0.5) * _WorldSeamNoiseWidth;
}

float WorldSeamRedWeight(float distanceToSeam)
{
    float feather = max(_WorldSeamFeather, 0.001);
    return 1.0 - smoothstep(-feather, feather, distanceToSeam);
}

float WorldSeamEdge(float distanceToSeam)
{
    return (1.0 - smoothstep(0.0, max(_WorldSeamEdgeWidth, 0.001),
                              abs(distanceToSeam))) * _WorldSeamEdgeStrength;
}

#endif
