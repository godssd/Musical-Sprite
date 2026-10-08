Shader "MusicalSprite/HoldNoteClippedSprite"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ClipX ("Platform Edge World X", Float) = 0
        _VisibleSide ("Visible Side", Float) = 1
        _GlowReceive ("Shockwave Light Receive", Range(0, 2)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend One OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float worldX : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _ClipX;
            float _VisibleSide;
            float _GlowReceive;

            // 冲击波辉光：全局参数（Shader.SetGlobal*），与 Note / ScenePropSprite 等 shader 同款。
            // ⛔ 这些不能写进上面的 Properties 块 —— 材质默认值会覆盖 SetGlobal 写入的全局值。
            float  _ShockGlowEnabled;
            half4  _ShockGlowColorRed;
            half4  _ShockGlowColorBlue;
            float4 _ShockGlowParamsRed;   // xyz = 红墙世界位置, w = 影响半径
            float4 _ShockGlowParamsBlue;  // xyz = 蓝墙世界位置, w = 影响半径
            float4 _ShockGlowStrengths;   // x = 红强度, y = 蓝强度

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldPos = wp;
                o.worldX = wp.x;
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Clip pixels, not endpoints: the visible ribbon keeps its original slope.
                clip((i.worldX - _ClipX) * _VisibleSide);
                fixed4 color = tex2D(_MainTex, i.uv) * i.color;
                color.rgb *= color.a;

                // ---- 冲击波辉光染色（双光源，与场景 shader 同款）----
                // 本 shader 是预乘 alpha 输出（color.rgb *= color.a），加光时也要乘 alpha，
                // 否则透明区域会被凭空点亮、出现亮块。
                if (_ShockGlowEnabled > 0.5 && _GlowReceive > 0.001)
                {
                    float dR = distance(i.worldPos, _ShockGlowParamsRed.xyz);
                    float aR = saturate(1.0 - dR / max(0.0001, _ShockGlowParamsRed.w));
                    aR = aR * aR;
                    float dBl = distance(i.worldPos, _ShockGlowParamsBlue.xyz);
                    float aB = saturate(1.0 - dBl / max(0.0001, _ShockGlowParamsBlue.w));
                    aB = aB * aB;

                    float3 lit = _ShockGlowColorRed.rgb  * (aR * _ShockGlowStrengths.x)
                               + _ShockGlowColorBlue.rgb * (aB * _ShockGlowStrengths.y);
                    color.rgb += lit * _GlowReceive * color.a;
                }
                return color;
            }
            ENDCG
        }
    }
}
