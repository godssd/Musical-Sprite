Shader "MusicalSprite/UI/ComboPaper" {
 Properties {
  [PerRendererData] _MainTex ("Glyph atlas",2D)="white" {}
  _Grey ("Paper grey",Range(0,1))=0
  _StencilComp("Stencil comparison",Float)=8
  _Stencil("Stencil ID",Float)=0
  _StencilOp("Stencil operation",Float)=0
  _StencilWriteMask("Stencil write",Float)=255
  _StencilReadMask("Stencil read",Float)=255
  _ColorMask("Colour mask",Float)=15
 }
 SubShader {
  Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float4 world:TEXCOORD1;};
   sampler2D _MainTex;float4 _ClipRect;float _Grey;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   fixed4 frag(v2f i):SV_Target {
    fixed4 c=tex2D(_MainTex,i.uv)*i.color;
    float grey=dot(c.rgb,float3(.2126,.7152,.0722));c.rgb=lerp(c.rgb,grey.xxx,_Grey);
    #ifdef UNITY_UI_CLIP_RECT
    c.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    return c;
   }
   ENDCG
  }
 }
}
