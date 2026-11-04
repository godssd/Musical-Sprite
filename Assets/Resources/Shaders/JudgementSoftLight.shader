Shader "MusicalSprite/JudgementSoftLight"
{
    Properties
    {
        _NearColor ("Near colour", Color) = (1,0.34,0.30,1)
        _FarColor ("Far colour", Color) = (0.60,0.49,0.78,1)
        _CoreColor ("Core colour", Color) = (1,0.94,0.84,1)
        _Strength ("Strength", Range(0,1)) = 1
        _HitMix ("Hit mix", Range(0,1)) = 0
        _IdleParams ("Idle parameters", Vector) = (0.9,1,0.18,0)
        _RailShape ("Rail shape", Vector) = (0.96,0.26,125,5.5)
        _RailBody ("Solid emitter half width and rim", Vector) = (.19,.018,0,0)
        _EdgeShape ("Edge shape", Vector) = (0.68,0.30,190,5)
        _CoreFade ("Independent core reach and falloff", Vector) = (.58,1.7,0,0)
        _FadeParams ("Fade parameters", Vector) = (1.55,0.70,0.62,0)
        _HueParams ("Hue parameters", Vector) = (0.78,0.04,0.90,0)
        _WashParams ("Wash parameters", Vector) = (0.14,0.21,0,0)
        [HideInInspector] _Kind ("Shape kind", Float) = 0
        [HideInInspector] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha [_DstBlend]
        Pass
        {
            Name "SoftJudgement"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _NearColor;
                float4 _FarColor;
                float4 _CoreColor;
                float _Strength;
                float _HitMix;
                float _Kind;
                float _DstBlend;
                float4 _IdleParams;
                float4 _RailShape;
                float4 _RailBody;
                float4 _EdgeShape;
                float4 _CoreFade;
                float4 _FadeParams;
                float4 _HueParams;
                float4 _WashParams;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }
            float EndFeather(float t, float width)
            {
                return smoothstep(0.0, width, t) * (1.0 - smoothstep(1.0 - width, 1.0, t));
            }
            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = saturate(input.uv);
                float u = uv.x;
                float across = uv.y * 2.0 - 1.0;
                float tail = pow(1.0 - u, max(0.1, _FadeParams.x)) * (1.0 - smoothstep(_FadeParams.y, 1.0, u));
                float3 tint = lerp(_NearColor.rgb, _FarColor.rgb, smoothstep(_HueParams.y, _HueParams.z, u) * _HueParams.x);
                float alpha;
                float3 color;
                if (_Kind < 0.5)
                {
                    float2 aa = max(fwidth(uv), float2(0.001, 0.001));
                    alpha = EndFeather(u, aa.x) * EndFeather(uv.y, aa.y) * _IdleParams.x;
                    color = _NearColor.rgb * _IdleParams.y;
                }
                else if (_Kind < 1.5)
                {
                    // Structural guides remain readable across the lane; the hit-light falloff
                    // must not also erase the permanent track structure.
                    float end = 1.0 - smoothstep(0.90,1.0,u);
                    float aa=max(fwidth(across),.05);
                    float feather=1-smoothstep(max(0,1-aa),1,abs(across));
                    alpha = end * lerp(1.0,0.65,u) * feather * _IdleParams.z;
                    color = lerp(float3(0.79, 0.77, 0.70), _NearColor.rgb, 0.24);
                }
                else if (_Kind < 2.5)
                {
                    float lateral = u * 2.0 - 1.0;
                    float core = exp2(-_RailShape.z * lateral * lateral);
                    float halo = max(0.0, exp2(-_RailShape.w * lateral * lateral) - exp2(-_RailShape.w)) * _RailShape.y;
                    halo *= 1-smoothstep(.65,1,abs(lateral));
                    float aa=max(fwidth(lateral),.002);
                    float body=1-smoothstep(max(0,_RailBody.x-aa),_RailBody.x+aa,abs(lateral));
                    float inner=1-smoothstep(max(0,_RailBody.x-_RailBody.y-aa),_RailBody.x,abs(lateral));
                    float end=EndFeather(uv.y,max(fwidth(uv.y),.001));
                    alpha=(body*_RailShape.x*end+halo*EndFeather(uv.y,.045))*min(1.0,_Strength);
                    // The emitting surface keeps its straight contour and coloured
                    // lip. Only the outside spill is soft, not the whole emitter.
                    color=lerp(_NearColor.rgb,_CoreColor.rgb,inner*.90);
                }
                else if (_Kind < 3.5)
                {
                    float taper = max(0.1, 1.0 - u * _FadeParams.z);
                    float d = across / taper;
                    float core = exp2(-_EdgeShape.z * d * d);
                    float halo = max(0.0, exp2(-_EdgeShape.w * d * d) - exp2(-_EdgeShape.w));
                    float junction=lerp(.30,1,smoothstep(0,.055,u));
                    float coreTail=pow(saturate(1-u/max(.05,_CoreFade.x)),max(.2,_CoreFade.y));
                    coreTail*=1-smoothstep(_CoreFade.x*.65,_CoreFade.x,u);
                    float coreLight=core * _EdgeShape.x * junction * coreTail;
                    float haloLight=halo * _EdgeShape.y * tail;
                    alpha = (coreLight+haloLight) * min(1.0, _Strength);
                    color = lerp(tint, _CoreColor.rgb, saturate(coreLight/max(.0001,coreLight+haloLight)));
                }
                else
                {
                    float nearFade = 1.0;
                    float soft = max(0.0, exp2(-2.5 * across * across) - exp2(-2.5));
                    alpha = nearFade * tail * soft * _Strength * lerp(_WashParams.x, _WashParams.y, _HitMix);
                    color = lerp(float3(0.92,0.94,0.98), tint, saturate(_HitMix));
                }
                // Strength above one remains a useful brightness control instead of only clipping alpha.
                if (_Kind > 1.5 && _Kind < 3.5) color *= 1.0+max(0.0,_Strength-1.0)*.30;
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
