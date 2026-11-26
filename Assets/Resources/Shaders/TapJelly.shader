Shader "MusicalSprite/TapJelly"
{
    Properties
    {
        [MainTexture] _MainTex ("Jelly artwork", 2D) = "white" {}
        [MainColor] _Color ("Tint", Color) = (1,1,1,1)
        _JellyHit ("Compression, light, sweep, travel sign", Vector) = (0,0,0,0)
        _TapImpact ("Light progress, strength, travel sign, fragment progress", Vector) = (0,0,0,0)
        _TapLift ("Ascending hit light", Range(0, 1)) = 0
        _RailX ("Judgement line world X", Float) = -10000
    }
    SubShader
    {
        // Both versions of the judgement rail finish at or below Transparent+22.
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+23" "RenderType"="Transparent" }
        Cull Back
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _JellyHit;
                float4 _TapImpact;
                float _TapLift;
                float _RailX;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half railOverlap : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float squeeze = _JellyHit.x;
                i.positionOS.x *= 1.0 - squeeze;
                float stretch = 1.0 + 0.65 * squeeze;
                float2 coverage = _TapImpact.y > 0.0 ? float2(2.55, 1.62) : float2(1.0, 1.0);
                // Grow the canvas towards the top of the arena. The painted note stays centred.
                float topMargin = 0.46 * _TapLift;
                coverage.y += topMargin;
                i.positionOS.xz *= coverage;
                i.positionOS.z = (i.positionOS.z + 0.5 * topMargin) * stretch;
                o.positionCS = TransformObjectToHClip(i.positionOS);
                o.uv = (i.uv - 0.5) * coverage + 0.5;
                o.uv.y += 0.5 * topMargin;
                float width = max(0.001, length(TransformObjectToWorldDir(float3(1, 0, 0), false)));
                float centerX = TransformObjectToWorld(float3(0, 0, 0)).x;
                o.railOverlap = 1.0 - smoothstep(0.08, 0.64, abs(centerX - _RailX) / width);
                return o;
            }

            // A tapered drop with an amber belly and a small offset highlight.
            half2 droplet(float2 p, float2 origin, float2 travel, float progress, float size)
            {
                float ease = 1.0 - (1.0 - progress) * (1.0 - progress);
                float2 axis = normalize(travel);
                float2 delta = p - (origin + travel * ease);
                float2 local = float2(dot(delta, axis), dot(delta, float2(-axis.y, axis.x))) / size;
                local.y /= 0.57 + 0.21 * clamp(local.x, -1.0, 1.0);
                float outline = length(local) - 1.0;
                float aa = max(fwidth(outline), 0.04);
                half mask = 1.0 - smoothstep(-aa, aa, outline);
                half shine = 1.0 - smoothstep(0.04, 0.47, length(local - float2(0.30, 0.19)));
                return half2(mask, shine * mask);
            }

            half4 contactSplash(float2 uv)
            {
                float t = _TapImpact.x;
                float2 p = (uv - 0.5) * float2(_TapImpact.z, 1.63);
                float extent = _TapImpact.y;
                float travel = saturate(t / 0.83);
                float pop = smoothstep(0.0, 0.10, t) * (1.0 - smoothstep(0.36, 0.84, t));
                // The outside shoulder overlaps the lane character. Let the main bead recoil
                // into the visible playfield, leaving the next note's paw at lane centre clear.
                half2 drops = droplet(p, float2(-0.42, 0.43), float2(-0.47, 0.29) * extent, travel, 0.119);
                drops += droplet(p, float2(0.44,-0.29), float2(0.39,-0.30) * extent, travel, 0.087);
                if (extent > 0.9)
                {
                    drops += droplet(p, float2(-0.30,-0.60), float2(-0.31,-0.25), travel, 0.079);
                    drops += droplet(p, float2(0.50, 0.10), float2(0.49, 0.10), travel, 0.062);
                }
                // A short crescent follows the contact shoulder, opening as the jelly rebounds.
                float2 curve = float2(p.x, max(abs(p.y) - 0.28, 0.0));
                float radius = 0.5 + 0.24 * (1.0 - (1.0 - t) * (1.0 - t));
                float d = abs(length(curve) - radius);
                float shoulder = smoothstep(0.10, 0.44, -p.x) * smoothstep(-0.32, 0.22, p.y);
                float sector = max(shoulder, 0.55 * smoothstep(0.05, 0.52, p.x))
                             * (1.0 - smoothstep(0.50, 0.79, abs(p.y)));
                float pulse = (1.0 - smoothstep(0.1, 0.55, t)) * extent * sector;
                float width = lerp(0.034, 0.012, t) * (0.45 + 0.55 * sector);
                float aa = max(fwidth(d), 0.003);
                half rim = (1.0 - smoothstep(width - aa, width + aa, d)) * pulse;
                half halo = exp2(-38.0 * d) * pulse;
                half3 amber = _TapImpact.z < 0 ? half3(1.0, 0.46, 0.105) : half3(1.0, 0.69, 0.25);
                half3 milk = half3(1.0, 0.91, 0.64);
                half3 hue = lerp(amber, milk, saturate(drops.y * 0.92 + rim * 0.84));
                return half4(hue, saturate(drops.x * pop * extent + rim * 0.9 + halo * 0.32));
            }

            // A pinched, faceted flake. The two faces catch different amounts of light
            // as it turns and slows after release. Coordinates use note widths.
            half2 risingFlake(float2 p, float t, float2 origin, float2 travel, float size, float lean)
            {
                t = saturate(t);
                // Initial kick, then a slower drift and roll as the fragment dims.
                float rise = 0.70 * (1.0 - (1.0 - t) * (1.0 - t) * (1.0 - t)) + 0.30 * t;
                float2 q = p - origin - travel * rise;
                q.x -= q.y * (lean + t * 0.52);
                q /= size;
                float edge = abs(q.x) * 1.7 + abs(q.y + q.x * 0.36) - 1.0;
                float aa = max(fwidth(edge), 0.05);
                half shape = (1.0 - smoothstep(-aa, aa, edge)) * smoothstep(0.0, 0.09, t)
                           * (1.0 - smoothstep(0.40, 1.0, t));
                return half2(shape, shape * smoothstep(-0.24, 0.04, q.x + q.y * 0.28));
            }

            half4 upwardLight(float2 uv, out half shellLight)
            {
                float t = _TapImpact.x;
                float strength = _TapImpact.y;
                float2 p = float2((uv.x - 0.5) * -_TapImpact.z, uv.y);
                float rise = 1.0 - (1.0 - t) * (1.0 - t);
                float pulse = smoothstep(0.0, 0.12, t) * (1.0 - smoothstep(0.36, 0.94, t)) * strength;
                // A warm front climbs both rims; the paw keeps its painted contrast.
                float faceY = 0.06 + 1.39 * rise;
                float rimX = 0.43 - 0.18 * smoothstep(0.28, 0.50, abs(uv.y - 0.5));
                float rim = exp2(-12.0 * abs(abs(p.x) - rimX));
                shellLight = pulse * exp2(-5.0 * abs(uv.y - faceY)) * (0.12 + 1.35 * rim);

                float h = p.y - 0.83;
                float height = (0.13 + 0.53 * rise) * strength;
                float v = saturate(h / height);
                float path = 0.23 - 0.12 * v + 0.045 * sin(5.5 * v + 2.2 * t);
                float spread = lerp(0.18, 0.042, v);
                float offset = (p.x - path) / spread;
                float envelope = smoothstep(-0.08, 0.09, h) * (1.0 - smoothstep(0.52, 1.0, v)) * pulse;
                half amber = exp2(-1.3 * offset * offset) * envelope;
                half core = exp2(-9.0 * offset * offset) * envelope;
                half echo = exp2(-60.0 * abs(p.x + 0.17 - 0.11 * v)) * envelope * (1.0 - v);

                float fragments = _TapImpact.w;
                float2 metric = float2(p.x, (p.y - 0.5) * 1.63)
                              * float2(1.0 - _JellyHit.x, 1.0 + 0.65 * _JellyHit.x);
                half2 chips = risingFlake(metric, (fragments - 0.08) / 0.91, float2(0.21,0.71), float2(0.14,0.75)*strength, 0.079, 0.22);
                chips += risingFlake(metric, (fragments - 0.16) / 0.79, float2(-0.17,0.67), float2(-0.17,0.54)*strength, 0.067,-0.48);
                chips += risingFlake(metric, (fragments - 0.12) / 0.83, float2(0.39,0.44), float2(0.24,0.60)*strength, 0.052, 0.40);
                if (strength > 0.9)
                {
                    chips += risingFlake(metric, (fragments - 0.19) / 0.81, float2(0.01,0.78), float2(-0.09,0.74), 0.050,-0.28);
                    chips += risingFlake(metric, (fragments - 0.09) / 0.82, float2(-0.36,0.48), float2(-0.11,0.48), 0.056, 0.16);
                }
                half lightAlpha = saturate(0.58 * amber + 0.52 * core + 0.37 * echo);
                half3 lightColor = lerp(half3(1.12,0.34,0.06), half3(1.52,1.02,0.45), saturate(core + echo));
                half3 chipColor = lerp(half3(1.08,0.47,0.12), half3(1.4,1.19,0.72), saturate(chips.y));
                half chipAlpha = saturate(chips.x);
                half behind = lightAlpha * (1.0 - chipAlpha);
                // Return premultiplied light for composition behind the shell.
                return half4(chipColor * chipAlpha + lightColor * behind, chipAlpha + behind);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 body = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(i.uv));
                if (any(abs(i.uv - 0.5) > 0.5)) body.a = 0;
                float across = _JellyHit.w > 0 ? 1.0 - i.uv.x : i.uv.x;
                float arc = across + 0.32 * pow(abs(i.uv.y - 0.5) * 2.0, 2.0);
                float dist = abs(arc - _JellyHit.z);
                half shell = smoothstep(0.2, 0.85, body.a);
                half glint = (exp2(-13.0 * dist) + 0.24 * exp2(-5.0 * dist)) * _JellyHit.y;
                half3 color = body.rgb * _Color.rgb + glint * lerp(0.20, 1.0, shell) * half3(1.0, 0.60, 0.19);
                // The original two-sided sprite blended twice. Preserve its resting optical density
                // with a single front-facing pass so the moving reflection is applied only once.
                half opacity = body.a * (2.0 - body.a);
                // The judgement emitter is much brighter than the floor. Give the amber face
                // enough density over it for the paw and shell to stay in the foreground.
                half contact = i.railOverlap * smoothstep(0.025, 0.16, body.a);
                opacity = lerp(opacity, max(opacity, 0.83), contact);
                color += contact * (1.0 - shell) * half3(0.15, 0.045, 0.0);
                half alpha = saturate(opacity + glint * shell * 0.15) * _Color.a;
                [branch] if (_TapImpact.y > 0)
                {
                    half4 fx = contactSplash(i.uv);
                    if (_TapLift > 0.0)
                    {
                        half shellLight;
                        half4 lift = upwardLight(i.uv, shellLight) * _TapLift;
                        color += shellLight * _TapLift * half3(1.38, 0.74, 0.19);
                        half blended = fx.a + lift.a * (1.0 - fx.a);
                        fx.rgb = (fx.rgb * fx.a + lift.rgb * (1.0 - fx.a)) / max(blended, 0.0001);
                        fx.a = blended;
                    }
                    half behind = fx.a * (1.0 - alpha);
                    half combined = alpha + behind;
                    return half4((color * alpha + fx.rgb * behind) / max(combined, 0.0001), combined);
                }
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
