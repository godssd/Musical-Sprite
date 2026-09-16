Shader "MusicalSprite/AdditiveSprite"
{
    // 极简加法混合 Sprite 着色器：采样 Sprite 贴图，按顶点色(_Color)着色，加法混合到背景。
    // 用于音符光晕 / 命中光束 / 光碎 / 能量槽 glow / Slide 柔光带。
    // 无需任何依赖：SpriteRenderer 的 color（含 alpha）会作为顶点色传入，控制颜色与强度。
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        Blend One One                       // 加法混合：发光叠加到背景
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                float4 color    : COLOR;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = TRANSFORM_TEX(IN.texcoord, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, IN.texcoord);
                // 用贴图 alpha 塑形柔光边缘（边缘 alpha≈0 → 贡献≈0），再乘顶点色/强度
                tex.rgb *= tex.a;
                return tex * IN.color;
            }
            ENDCG
        }
    }
}
