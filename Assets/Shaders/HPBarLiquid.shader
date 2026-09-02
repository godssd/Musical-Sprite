Shader "MusicalSprite/UI/HPBarLiquid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Fill ("Fill", Range(0, 1)) = 1
        _Flip ("Flip (0=left->right, 1=right->left)", Float) = 0
        _WaveAmp ("Slosh / Wave Amplitude", Float) = 0
        _SloshFreq ("Slosh Frequency", Float) = 2.2
        _SloshSpeed ("Slosh Speed", Float) = 7
        _WaveFreq ("Ripple Frequency", Float) = 14
        _WaveSpeed ("Ripple Speed", Float) = 5
        _RippleScale ("Ripple Scale (high-freq twist)", Range(0, 1)) = 0.15
        _AmbientWave ("Ambient Wave", Range(0, 0.03)) = 0.008
        _TopColor ("Top Color", Color) = (1.00, 0.15, 0.00, 1)
        _BottomColor ("Bottom Color", Color) = (1.00, 0.15, 0.00, 1)
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.05)) = 0.004
        _CrestColor ("Crest Color", Color) = (1, 0.35, 0.25, 1)
        _CrestWidth ("Crest Width", Range(0, 0.3)) = 0.08
        _CrestIntensity ("Crest Intensity", Range(0, 3)) = 1.0
        _SurfaceGlow ("Surface Glow", Range(0, 2)) = 0.4
        _SurfaceDarken ("Surface Darken Color", Color) = (1.00, 0.15, 0.00, 1)
        _SurfaceDarkenRange ("Surface Darken Range", Range(0, 0.5)) = 0
        _TopGlossColor ("Top Gloss Color", Color) = (1.00, 0.22, 0.06, 1)
        _TopGlossRange ("Top Gloss Range", Range(0, 0.5)) = 0.22
        _TopGlossIntensity ("Top Gloss Intensity", Range(0, 1)) = 0

        _BlobFreq ("Blob Frequency", Float) = 5.5
        _BlobThreshold ("Blob Threshold", Range(0, 1)) = 0.42
        _BlobSoftness ("Blob Softness", Range(0, 1)) = 0.22
        _BlobIntensity ("Blob Intensity", Range(0, 1)) = 0

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _ClipRect;

            float  _Fill;
            float  _Flip;
            float  _WaveAmp;
            float  _SloshFreq;
            float  _SloshSpeed;
            float  _WaveFreq;
            float  _WaveSpeed;
            float  _RippleScale;
            float  _AmbientWave;
            float4 _TopColor;
            float4 _BottomColor;
            float  _EdgeSoftness;
            float4 _CrestColor;
            float  _CrestWidth;
            float  _CrestIntensity;
            float  _SurfaceGlow;
            float4 _SurfaceDarken;
            float  _SurfaceDarkenRange;
            float4 _TopGlossColor;
            float  _TopGlossRange;
            float  _TopGlossIntensity;
            float  _BlobFreq;
            float  _BlobThreshold;
            float  _BlobSoftness;
            float  _BlobIntensity;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // _Flip=0：液体靠左，右侧是液面；_Flip=1：镜像，液体靠右，左侧是液面
                float u = lerp(uv.x, 1.0 - uv.x, step(0.5, _Flip));

                float t = _Time.y;

                // 上下两端略微收窄，让液体看起来"贴着容器壁"
                float taper = 0.65 + 0.35 * sin(uv.y * 3.14159265);

                // 横置液体晃动模型：液体沿填充方向(x)左右涨落为主，
                // 表面(y)起伏 + 倾斜为辅，模拟酒杯横过来荡漾。
                // _WaveAmp 由 C# 根据扣血量激发，再指数衰减至平静。

                // ① 主晃：整体沿填充方向(x)进退（左红右液面，整条液面一起左右荡），速度由 _SloshSpeed
                float s = sin(t * _SloshSpeed) * _WaveAmp * taper;
                // ② 倾斜：液面线在 y 上带斜率（上半进/下半退），模拟横置重力荡漾，速度由 _SloshFreq
                float tilt = cos(t * _SloshFreq) * _WaveAmp * 0.5 * (uv.y - 0.5);
                // ③ 表面起伏：沿 y 的轻微波纹（替代旧的高频扭曲细纹，幅度远小于主晃）
                float surf = sin(uv.y * _WaveFreq * 0.7 + t * _WaveSpeed * 0.8) * _WaveAmp * 0.10;

                float boundary = _Fill + s + tilt + surf;

                // 环境微波动：无扣血时液面也保持轻微不规则
                float ambient = sin(uv.y * _WaveFreq * 0.65 + t * _WaveSpeed * 0.35) * _AmbientWave;
                boundary += ambient;

                // 防止液体越出黑槽（满血不过量、空血不反向）
                boundary = clamp(boundary, 0.0, 1.0);

                // d > 0 表示处于液体一侧
                float d = boundary - u;

                // 抗锯齿软边
                float alpha = smoothstep(-_EdgeSoftness, _EdgeSoftness, d);
                if (alpha <= 0.002) discard;

                // 沿填充方向(u)的渐变：满端(右侧/液面端=1)用 TopColor，空端用 BottomColor
                float4 liquid = lerp(_BottomColor, _TopColor, u);

                // 靠近液面/空槽的一侧加深，做出参考图中右侧深红/暗部效果
                float darkenRange = max(_SurfaceDarkenRange, 0.001);
                float darkenFactor = smoothstep(0.0, darkenRange, d);
                liquid.rgb = lerp(_SurfaceDarken.rgb, liquid.rgb, darkenFactor);

                // 液面高光（波峰/泡沫），只在液面附近出现
                float w = max(_CrestWidth, 0.001);
                float crest = 1.0 - smoothstep(0.0, w, d);
                crest *= crest;
                liquid.rgb += _CrestColor.rgb * crest * _CrestIntensity * (0.35 + _WaveAmp * 5.0);

                // 液面整体微光
                float glow = 1.0 - smoothstep(0.0, w * 2.5, d);
                liquid.rgb += _CrestColor.rgb * glow * _SurfaceGlow * 0.35;

                // 顶部高光：模拟参考图里液体上方的白色反光带
                float glossRange = max(_TopGlossRange, 0.001);
                float glossCenter = 1.0 - glossRange * 0.55;
                float gloss = 1.0 - smoothstep(0.0, glossRange, abs(uv.y - glossCenter));
                // 加一点横向不规则，避免太像塑料
                gloss *= 0.7 + 0.3 * sin(uv.x * 14.0 + _Time.y * 1.5);
                liquid.rgb += _TopGlossColor.rgb * saturate(gloss) * _TopGlossIntensity;

                half4 color = liquid * IN.color;
                color.a *= alpha;

                // 大块血球（可选）：_BlobIntensity=0 时就是纯色填满
                float blobMask = 1.0;
                if (_BlobIntensity > 0.001)
                {
                    float2 bp = uv * max(_BlobFreq, 0.001);
                    float blobs  = sin(bp.x) * sin(bp.y);
                    blobs += sin(bp.x * 1.5 + bp.y * 0.5) * 0.5;
                    blobs += sin(bp.x * 0.5 - bp.y * 1.2) * 0.5;
                    blobs = saturate((blobs / 2.0) * 0.5 + 0.5);
                    blobMask = smoothstep(_BlobThreshold, _BlobThreshold + max(_BlobSoftness, 0.001), blobs);
                }
                color.a *= lerp(1.0, blobMask, _BlobIntensity);

                // 若给 Fill 配了遮罩贴图，用其 alpha 裁剪外形（无贴图时为白图 alpha=1）
                color.a *= tex2D(_MainTex, uv).a;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (color.a - 0.001);
                #endif

                return color;
            }
        ENDCG
        }
    }
}
