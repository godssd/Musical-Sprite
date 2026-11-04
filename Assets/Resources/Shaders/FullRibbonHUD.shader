Shader "MusicalSprite/UI/FullRibbonHUD" {
 Properties {
  [PerRendererData] _MainTex("Sprite",2D)="white"{}
  _StencilComp("Stencil comparison",Float)=8 _Stencil("Stencil",Float)=0
  _StencilOp("Stencil operation",Float)=0 _StencilWriteMask("Stencil write",Float)=255
  _StencilReadMask("Stencil read",Float)=255 _ColorMask("Color mask",Float)=15
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   #include "RibbonBoundary.cginc"
   struct appdata{float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   struct v2f{float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float4 world:TEXCOORD1;};
   sampler2D _MainTex;float4 _SpriteUV,_Tint,_ClipRect,_BarSize,_LowBoundary,_HighBoundary;
   float4 _EmptyColor,_LossColor,_HealColor,_FlashColor,_FlashBoundary0,_FlashBoundary1;
   float _Mode,_Monochrome,_Brightness,_Visibility,_SideSign,_Bow,_Interval,_Healing,_Flash,_Full,_PaperStrength,_LowerShade;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   float PaperNoise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);float4 h=frac(sin(float4(dot(a,float2(127.1,311.7)),dot(a+float2(1,0),float2(127.1,311.7)),dot(a+float2(0,1),float2(127.1,311.7)),dot(a+1,float2(127.1,311.7))))*43758.5453);return lerp(lerp(h.x,h.y,f.x),lerp(h.z,h.w,f.x),f.y);}
   fixed4 frag(v2f i):SV_Target{
    float inside=step(0,i.uv.x)*step(i.uv.x,1)*step(0,i.uv.y)*step(i.uv.y,1);
    fixed4 tex=tex2D(_MainTex,_SpriteUV.xy+saturate(i.uv)*_SpriteUV.zw);
    float3 pigment=lerp(tex.rgb,max(tex.r,max(tex.g,tex.b)).xxx,_Monochrome)*_Tint.rgb;
    if(_Mode>.5){
     float2 p=i.uv*_BarSize.xy;
     float lo=RibbonProgressSide(p.x,p.y,_LowBoundary,_SideSign,_Bow);
     float hi=RibbonProgressSide(p.x,p.y,_HighBoundary,_SideSign,_Bow);
     float remaining=max(1-step(.001,lo),_Full);
     float pending=step(0,lo)*(1-step(.001,hi))*_Interval;
     pigment=lerp(_EmptyColor.rgb,pigment,remaining);
     pigment=lerp(pigment,lerp(_LossColor.rgb,_HealColor.rgb,_Healing),pending);
     pigment*=_Brightness;
     float f0=RibbonProgressSide(p.x,p.y,_FlashBoundary0,_SideSign,_Bow);
     float f1=RibbonProgressSide(p.x,p.y,_FlashBoundary1,_SideSign,_Bow);
     pigment=lerp(pigment,_FlashColor.rgb,_Flash*step(0,f0)*(1-step(.001,f1)));
    }
    float2 paper=i.uv*(_Mode>.5?_BarSize.xy:float2(90,90));
    float grain=(PaperNoise(paper*.09)-.5)*.75+(PaperNoise(paper*.7)-.5)*.25;
    pigment*=1+grain*_PaperStrength;
    pigment*=lerp(1-_LowerShade,1,i.uv.y);
    fixed4 result=fixed4(pigment,tex.a*inside*_Visibility)*i.color;
    #ifdef UNITY_UI_CLIP_RECT
    result.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    return result;
   }
   ENDCG
  }
 }
}
