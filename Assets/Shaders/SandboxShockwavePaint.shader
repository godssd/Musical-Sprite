Shader "MusicalSprite/SandboxShockwavePaint"
{
    Properties
    {
        _BrushMask ("Broad hand-painted flow mask", 2D) = "gray" {}
        _ArtDark ("Side and rear", Color) = (0.3,0.025,0.06,1)
        _ArtBody ("Body", Color) = (0.95,0.16,0.075,1)
        _ArtLight ("Flow", Color) = (1.5,0.48,0.16,1)
        _ArtRim ("Structure edge", Color) = (2,1.4,0.54,1)
        _FlowSpeed ("Flow speed", Range(0,2)) = 0.45
        _FlowStrength ("Flow contrast", Range(0,1)) = 0.55
        _Erode ("Short rear erosion", Range(0,1)) = 0.42
        _Opacity ("Driven opacity", Float) = 0.18
        _Flash ("Driven flash", Float) = 0
        _BackX ("Driven back X", Float) = -1
        _FrontX ("Driven front X", Float) = 2.8
        _ZHalf ("Driven half span", Float) = 14.7
        _ArtWidthHalf ("Imported model half width", Float) = 0.1469
        _ArtHeightMax ("Imported model height", Float) = 0.0932
        [HideInInspector] _ColorDeep ("Legacy", Color) = (1,1,1,1)
        [HideInInspector] _ColorTip ("Legacy", Color) = (1,1,1,1)
        [HideInInspector] _ColorTail ("Legacy", Color) = (1,1,1,1)
        [HideInInspector] _TailPoint ("Legacy", Float) = 0
        [HideInInspector] _TailWidth ("Legacy", Float) = 0
        [HideInInspector] _VerticalAmount ("Legacy", Float) = 0
        [HideInInspector] _VerticalCenter ("Legacy", Float) = 0
        [HideInInspector] _VerticalWidth ("Legacy", Float) = 0
        [HideInInspector] _VerticalPower ("Legacy", Float) = 0
        [HideInInspector] _ArchHeight ("Legacy", Float) = 0
        [HideInInspector] _FadePower ("Legacy", Float) = 0
        [HideInInspector] _GradientPower ("Legacy", Float) = 0
        [HideInInspector] _GradientBalance ("Legacy", Float) = 0
        [HideInInspector] _EdgeGlow ("Legacy", Float) = 0
        [HideInInspector] _GlowIntensity ("Legacy", Float) = 0
        [HideInInspector] _GlowColor ("Legacy", Color) = (0,0,0,1)
        [HideInInspector] _GlowArch ("Legacy", Float) = 0
        [HideInInspector] _GlowFront ("Legacy", Float) = 0
        [HideInInspector] _GlowTailCut ("Legacy", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BrushMask);
            SAMPLER(sampler_BrushMask);
            float4 _ArtDark, _ArtBody, _ArtLight, _ArtRim;
            float _FlowSpeed, _FlowStrength, _Erode, _Opacity, _Flash, _BackX, _FrontX, _ZHalf;
            float _ArtWidthHalf, _ArtHeightMax;
            struct Attributes { float3 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionHCS:SV_POSITION; float3 local:TEXCOORD0; float3 normalWS:TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS);
                o.local = v.positionOS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float t = saturate((i.local.x - _BackX) / max(0.00001, _FrontX - _BackX));
                // WALL.fbx imports its ground-plan span into local Y and its height into local Z.
                float span = saturate(abs(i.local.y) / max(0.00001, _ArtWidthHalf));
                float height = saturate(i.local.z / max(0.00001, _ArtHeightMax));
                float top = smoothstep(0.28, 0.82, abs(normalize(i.normalWS).y));
                float time = _Time.y * _FlowSpeed;
                // A broad moving ridge travels from the contact face (t=1) to the rear (t=0).
                // It changes colour on the existing mesh; the silhouette is supplied by WALL.fbx.
                float travel = 1.0 - t;
                float phase = frac(travel * 1.9 - time * 0.7 + span * 0.12);
                float ridge = smoothstep(0.04, 0.24, phase)
                            * (1.0 - smoothstep(0.44, 0.80, phase));
                float brush = SAMPLE_TEXTURE2D(_BrushMask, sampler_BrushMask,
                    float2(travel * 0.67 - time * 0.18, span * 0.81 + height * 0.13)).r;
                float broad = ridge * (0.62 + 0.38 * brush);
                float3 col = lerp(_ArtDark.rgb, _ArtBody.rgb, 0.42 + t * 0.36 + top * 0.12);
                col = lerp(col, _ArtLight.rgb, broad * _FlowStrength * (0.50 + 0.50 * top));
                float edge = smoothstep(0.87, 0.995, t) * (0.42 + top * 0.58);
                col = lerp(col, _ArtRim.rgb, edge * 0.35);
                float erosion = SAMPLE_TEXTURE2D(_BrushMask, sampler_BrushMask,
                    float2(span * 0.83 + _Time.y * 0.035, t * 0.72 + 0.15)).b;
                float rear = 1.0 - smoothstep(0.025, 0.15, t);
                float alpha = saturate(_Opacity * 4.4) * (0.86 + top * 0.13);
                alpha *= 1.0 - rear * _Erode * (0.17 + erosion * 0.34);
                col = lerp(col, float3(1.0, 1.0, 1.0), saturate(_Flash) * 0.68);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
}
