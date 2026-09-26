Shader "MusicalSprite/HoldNoteClippedSprite"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _ClipX ("Platform Edge World X", Float) = 0
        _VisibleSide ("Visible Side", Float) = 1
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
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _ClipX;
            float _VisibleSide;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldX = mul(unity_ObjectToWorld, v.vertex).x;
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
                return color;
            }
            ENDCG
        }
    }
}
