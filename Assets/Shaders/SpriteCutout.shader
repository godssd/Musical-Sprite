// MusicalSprite/SpriteCutout
// A drop-in replacement for the built-in "Sprites/Default" that writes depth
// (ZWrite On) and uses alpha clipping instead of alpha blending.
//
// Why this exists:
//   The default sprite material is transparent and does NOT write depth. Two
//   transparent objects (e.g. a tree sprite and a fog plane) therefore cannot
//   intersect per-pixel -- Unity can only sort them whole-object by distance.
//   With alpha clipping + ZWrite, the tree becomes "opaque-like" for depth
//   purposes, so a transparent fog plane behind/around it correctly occludes
//   the part of the tree that is farther away. This gives the COTL-style
//   "scene prop sinking into the fog" spatial relationship.
//
// Usage:
//   Assign this material to a SpriteRenderer. SpriteRenderer automatically binds
//   the sprite texture to _MainTex and the tint to the vertex COLOR stream, so
//   no extra wiring is needed. Per-instance sprites share this one material.

Shader "MusicalSprite/SpriteCutout"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [PerRendererData] _Color ("Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        // AlphaTest queue (2450) draws AFTER opaque geometry but BEFORE
        // transparent geometry (fog at 3000), and ZWrite On lets the sprite
        // occlude the fog behind it.
        Cull Off
        ZWrite On
        Blend Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;
            float4 _Color;
            float  _Cutoff;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * _Color * IN.color;
                // Hard cut for any pixel below the cutoff -> writes depth, no blend.
                clip(c.a - _Cutoff);
                return c;
            }
            ENDHLSL
        }
    }
}
