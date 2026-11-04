Shader "MusicalSprite/UI/FullRibbonHUD"
{
 Properties {
  [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
  _StencilComp ("Stencil Comparison", Float) = 8
  _Stencil ("Stencil ID", Float) = 0
  _StencilOp ("Stencil Operation", Float) = 0
  _StencilWriteMask ("Stencil Write Mask", Float) = 255
  _StencilReadMask ("Stencil Read Mask", Float) = 255
  _ColorMask ("Color Mask", Float) = 15
  [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha Clip", Float) = 0
 }
 SubShader {
  Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="False"}
  Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend SrcAlpha OneMinusSrcAlpha
  ColorMask [_ColorMask]
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;UNITY_VERTEX_INPUT_INSTANCE_ID};
   struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float4 world:TEXCOORD1;UNITY_VERTEX_OUTPUT_STEREO};
   sampler2D _MainTex;
   float4 _SpriteUV, _ClipRect, _Tint, _LossColor, _HealColor, _FlashColor, _EmptyColor;
   float4 _RevealSpan;
   float _Mode,_Flip,_Cursor,_Target,_Flash,_FlashLow,_FlashHigh,_TickWidth,_TickGlow,_TickVisible,_Monochrome;
   float _Brightness,_GlowRadiusX,_GlowRadiusY,_FlashGlow;
   float _Load,_IntroBlend,_IntroGlow,_HeadGlow;
   v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   fixed4 sampleShape(float2 uv) {
    float valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
    return tex2D(_MainTex,_SpriteUV.xy+saturate(uv)*_SpriteUV.zw)*valid;
   }
   float crossAlpha(float2 uv,float2 d) {
    return sampleShape(uv+float2(d.x,0)).a+sampleShape(uv-float2(d.x,0)).a+sampleShape(uv+float2(0,d.y)).a+sampleShape(uv-float2(0,d.y)).a;
   }
   fixed4 frag(v2f i):SV_Target {
    float2 uv=i.uv;fixed4 tex=sampleShape(uv);
    float x=lerp(uv.x,1-uv.x,_Flip);
    float globalX=_RevealSpan.x+uv.x*_RevealSpan.y;
    float revealX=lerp(globalX,1-globalX,_Flip);
    float reveal=1-step(_Load+.0001,revealX);
    float head=exp(-abs(revealX-_Load)*105)*step(.001,_IntroBlend);
    fixed3 pigment=lerp(tex.rgb,max(tex.r,max(tex.g,tex.b)).xxx,_Monochrome);
    fixed3 rgb=pigment*_Tint.rgb;
    float flashRegion=step(_FlashLow,x)*step(x,_FlashHigh);
    float tick=0;
    if(_Mode>.5) {
     float realEnd=min(_Cursor,_Target);
     float pending=step(realEnd,x)*step(x,max(_Cursor,_Target));
     rgb=lerp(_EmptyColor.rgb,tex.rgb,step(x,realEnd));
     rgb=lerp(rgb,lerp(_LossColor.rgb,_HealColor.rgb,step(_Cursor,_Target)),pending);
     rgb=lerp(rgb,_FlashColor.rgb,_Flash*flashRegion);
     rgb*=_Brightness;
     tick=(1-smoothstep(_TickWidth*.35,_TickWidth*.65,abs(x-_Cursor)))*_TickVisible;
     rgb=lerp(rgb,_FlashColor.rgb,tick);
    }
    fixed3 loadingWhite=fixed3(1,.965,.87);
    #ifndef UNITY_COLORSPACE_GAMMA
    loadingWhite=GammaToLinearSpace(loadingWhite);
    #endif
    rgb=lerp(rgb,loadingWhite,_IntroBlend);
    float core=tex.a*reveal;
    float dilated=tex.a;
    // Bounded 8-tap dilation only for the two bars / a brief loading overlay.
    if(_TickGlow+_Flash+_IntroBlend>.001) {
     float2 d=float2(_GlowRadiusX,_GlowRadiusY);
     dilated=crossAlpha(uv,d*.25)*.1+crossAlpha(uv,d*.5)*.075+crossAlpha(uv,d*.75)*.05+crossAlpha(uv,d)*.025;
    }
    float tickHalo=exp(-abs(x-_Cursor)/max(.001,_GlowRadiusX)) * _TickGlow*_TickVisible;
    float flashHalo=_Flash*_FlashGlow*flashRegion;
    float introHalo=_IntroBlend*(_IntroGlow+head*_HeadGlow);
    float glow=saturate(dilated*(tickHalo+flashHalo+introHalo)*2)*max(reveal,head);
    fixed3 glowColor=lerp(_FlashColor.rgb,loadingWhite,_IntroBlend);
    float alpha=saturate(core+glow*(1-core));
    rgb=lerp(glowColor,rgb,core/max(.0001,alpha));
    fixed4 result=fixed4(rgb,alpha)*i.color;
    #ifdef UNITY_UI_CLIP_RECT
    result.a*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    #ifdef UNITY_UI_ALPHACLIP
    clip(result.a-.001);
    #endif
    return result;
   }
   ENDCG
  }
 }
}
