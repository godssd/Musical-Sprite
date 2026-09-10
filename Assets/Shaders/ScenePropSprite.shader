Shader "MusicalSprite/ScenePropSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        // 贴图现为本项目重绘的「绿树 + 透明底」(RGBA 带 alpha，无白底)。
        // 用 _Cutoff 把低 alpha 像素当透明裁掉；亮度键(_WhiteKey)对现贴图已无作用但保留兼容。
        // 树保留为不透明/裁透明 -> 写深度 -> 吃全屏雾。
        _WhiteKey ("White Key (luminance)", Range(0.5, 1.0)) = 0.9
        _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "DisableBatching" = "True"
        }
        LOD 100
        Cull Off
        ZWrite On
        ZTest LEqual

        // ---- 主通道：不透明、写深度、对近白背景做 color-key 裁剪 ----
        // 走 UniversalForward，与平台/方块同一条雾管线；
        // 雾由场景里已有的 FullScreenPass 雾后处理统一计算（按 _CameraDepthTexture 反算世界 Y）。
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _Color;
            float4 _RendererColor;
            float  _WhiteKey;
            float  _Cutoff;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                o.color = v.color * _Color * _RendererColor;
                return o;
            }

            // 裁掉近白背景 或 低 alpha 像素；返回 <0 则 discard
            float DiscardMask(half4 tex)
            {
                half lum = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                // 近白 -> _WhiteKey - lum < 0 -> 裁掉（现贴图无白底，此路径惰性）
                // 低 alpha -> tex.a - _Cutoff < 0 -> 裁掉（现贴图主要靠此路径裁透明）
                return min(_WhiteKey - lum, tex.a - _Cutoff);
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(DiscardMask(tex));
                half3 col = tex.rgb * i.color.rgb;

                // 与 GroundEdge 同款 fake-light：把场景件统一压到 ~0.78 亮度带，
                // 让全屏雾的边缘渐变色加法在树/地形上表现一致。
                // 树是朝向相机的公告板（无有意义法线），用与 GroundEdge 顶面对应的
                // 固定衰减，不另开亮度调节旋钮，避免后续光效再不一致。
                col *= 0.78;

                return half4(col, tex.a * i.color.a);
            }
            ENDHLSL
        }

        // ---- 深度法线通道：给 SSAO 深度预通道写进 _CameraDepthTexture ----
        // 场景开了 SSAO -> URP 跑 DepthNormalOnlyPass，只渲染 LightMode=DepthNormals 的 Pass。
        // 没有它，Sprite 不进深度图，雾会采到身后背景深度（即之前"完全不吃雾"的根因）。
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float  _WhiteKey;
            float  _Cutoff;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = v.uv;
                return o;
            }

            float DiscardMask(half4 tex)
            {
                half lum = dot(tex.rgb, half3(0.299, 0.587, 0.114));
                return min(_WhiteKey - lum, tex.a - _Cutoff);
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                clip(DiscardMask(tex));
                // 公告板是朝向相机的平面，法线取朝相机方向 (0,0,1) 即可（仅影响 SSAO 质量，不影响雾）。
                return half4(half3(0.0, 0.0, 1.0), 0.0);
            }
            ENDHLSL
        }
    }
}
