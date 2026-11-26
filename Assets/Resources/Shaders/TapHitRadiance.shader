Shader "MusicalSprite/TapHitRadiance"
{
    Properties
    {
        _TapImpact ("Time, strength, travel sign", Vector) = (0,0,0,0)
        _JellyHit ("Jelly deformation", Vector) = (0,0,0,0)
        _RadianceGain ("Hit radiance", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+24" "RenderType"="Transparent" }
        Blend One One
        ColorMask RGB
        ZWrite Off
        ZTest LEqual
        Cull Back
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _TapImpact, _JellyHit;
                float _RadianceGain;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                // Margins for the emitting rim and the soft spill above the crown.
                float2 plane = i.positionOS.xz * float2(3.0, 2.26) + float2(0.0, 0.24);
                o.p = plane * float2(-_TapImpact.z, 1.63);
                i.positionOS.xz = plane * float2(1.0 - _JellyHit.x, 1.0 + 0.65 * _JellyHit.x);
                o.positionCS = TransformObjectToHClip(i.positionOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _TapImpact.x;
                float strength = _TapImpact.y;
                float rise = t * (2.0 - t);
                float contact = 0.49 * smoothstep(0.0, 0.08, t) * (1.0 - smoothstep(0.10, 0.23, t));
                float rebound = smoothstep(0.19, 0.36, t) * (1.0 - smoothstep(0.42, 0.89, t));
                float envelope = contact + rebound;
                float2 p = i.p;
                // Signed distance to the amber shell. Light falls off on both sides, leaving
                // the paw legible while allowing the silhouette to light the nearby floor.
                float d = length(float2(p.x, max(abs(p.y) - 0.34, 0.0))) - 0.47;
                float front = lerp(-0.55, 0.98, rise);
                float height = p.y - front;
                float edgePower = (0.25 + 0.95 * exp2(-4.8 * height * height))
                                * lerp(0.69, 1.0, smoothstep(-0.25, 0.30, p.x));
                float edge = abs(d);
                float core = exp2(-42.0 * edge) * edgePower;
                float near = exp2(-9.5 * edge) * edgePower;
                float spill = exp2(-4.2 * edge) * edgePower * smoothstep(-0.12, 0.12, d);

                // The crown follows the upward light on the playfield-facing shoulder.
                float tip = lerp(0.55, 1.27, rise);
                float2 delta = p - float2(0.20, tip);
                delta.x += 0.18 * delta.y;
                float crown = exp2(-dot(delta * delta, float2(30.0, 10.0)));
                float wick = exp2(-dot(delta * delta, float2(240.0, 35.0)));
                float tail = exp2(-dot(delta * delta, float2(7.5, 3.2)));
                float crownPulse = smoothstep(0.04, 0.26, t);
                half3 radiance = half3(1.75, 0.92, 0.32) * (core + 1.05 * wick * crownPulse)
                               + half3(0.93, 0.36, 0.065) * (near + 0.95 * crown * crownPulse)
                               + half3(0.49, 0.115, 0.023) * (spill + 0.8 * tail * crownPulse);
                return half4(radiance * (envelope * strength * strength * _RadianceGain), 0);
            }
            ENDHLSL
        }
    }
}
