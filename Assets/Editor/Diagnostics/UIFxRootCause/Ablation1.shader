Shader "Hidden/Diagnostics/UIFxAblation1"
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
   sampler2D _MainTex, _CurveTex, _DistanceTex;
   float4 _DistanceUV;float _HasDistance,_DistanceScale,_GlowWorld;
   float _HasProfile,_TickActivity,_LoadingTick,_Healing;float4 _MeterRange;
   float4 _SpriteUV, _ClipRect, _Tint, _LossColor, _HealColor, _FlashColor, _EmptyColor;
   float4 _RevealSpan;
   float _Mode,_Flip,_Cursor,_Target,_Flash,_FlashLow,_FlashHigh,_TickWidth,_TickGlow,_TickVisible,_Monochrome;
   float _Brightness,_GlowRadiusX,_GlowRadiusY,_FlashGlow,_PendingGlow,_PendingInnerLight;
   float4 _BarCurve;
   float _TickArc;
   float _Load,_IntroBlend,_IntroGlow,_HeadGlow,_ResolvePulse,_EntryVisible,_Identity;
   v2f vert(appdata v){v2f o;UNITY_SETUP_INSTANCE_ID(v);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   fixed4 sampleShape(float2 uv) {
    float valid=step(0,uv.x)*step(uv.x,1)*step(0,uv.y)*step(uv.y,1);
    return tex2D(_MainTex,_SpriteUV.xy+saturate(uv)*_SpriteUV.zw)*valid;
   }
   float crossAlpha(float2 uv,float2 d) {
    return sampleShape(uv+float2(d.x,0)).a+sampleShape(uv-float2(d.x,0)).a+sampleShape(uv+float2(0,d.y)).a+sampleShape(uv-float2(0,d.y)).a;
   }
   float softAlpha(float2 uv,float2 radius){
    float a=sampleShape(uv).a*.12,weightSum=.12;
    // Gaussian weights avoid the repeated hard silhouette of a uniform disk.
    [unroll] for(int k=0;k<32;k++){
     float angle=k*2.3999632,r=sqrt((k+.5)/32.0);
     float weight=exp(-r*r*4.5);
     a+=sampleShape(uv+radius*float2(cos(angle),sin(angle))*r).a*weight;
     weightSum+=weight;
    }
    return a/weightSum;
   }
   fixed4 frag(v2f i):SV_Target {
    float2 uv=i.uv;fixed4 tex=sampleShape(uv);
    float sourceX=lerp(uv.x,1-uv.x,_Flip);
    float x=(sourceX-_MeterRange.x)/max(.001,_MeterRange.y-_MeterRange.x);
    float globalX=_RevealSpan.x+uv.x*_RevealSpan.y;
    float revealX=lerp(globalX,1-globalX,_Flip);
    float reveal=max(1-step(_Load+.0001,revealX),_Identity);
    float head=exp(-abs(revealX-_Load)*105)*step(.001,_IntroBlend);
    fixed3 pigment=lerp(tex.rgb,max(tex.r,max(tex.g,tex.b)).xxx,_Monochrome);
    fixed3 rgb=pigment*_Tint.rgb;
    float tick=0;
    float cursorU=saturate(uv.x);
    float centerY=((_BarCurve.x*cursorU+_BarCurve.y)*cursorU+_BarCurve.z)*cursorU+_BarCurve.w;
    float2 profile=tex2D(_CurveTex,float2(saturate(uv.x),.5)).rg;
    centerY=lerp(centerY,(profile.x+profile.y)*.5,_HasProfile);
    float halfHeight=lerp(.24,max(.025,(profile.y-profile.x)*.5),_HasProfile);
    float crossSection=(uv.y-centerY)/halfHeight;
    // All health boundaries use the same curved cross-section, not a curved
    // marker laid over a straight vertical cut. Its silhouette remains the PNG.
    float curveOffset=_TickArc*(1-saturate(crossSection*crossSection));
    float healthX=x-curveOffset;
    float flashRegion=step(_FlashLow,healthX)*step(healthX,_FlashHigh);
    float tickPosition=clamp(_Cursor,.0042,.9958);
    float tickDistance=abs(healthX-tickPosition);
    float pending=0;
    float3 pendingColor=lerp(_LossColor.rgb,_HealColor.rgb,_Healing);
    float2 smallStep=float2(_GlowRadiusX,_GlowRadiusY)*.17;
    float rim=0;
    float interval=step(.00001,abs(_Cursor-_Target))*step(.5,_Mode)*(1-step(.001,_IntroBlend));
    if(_IntroBlend+interval>.001){
     float inside=min(min(sampleShape(uv+float2(smallStep.x,0)).a,sampleShape(uv-float2(smallStep.x,0)).a),
                      min(sampleShape(uv+float2(0,smallStep.y)).a,sampleShape(uv-float2(0,smallStep.y)).a));
     rim=saturate(tex.a-inside);
    }
    if(_Mode>.5) {
     float realEnd=min(_Cursor,_Target);
     pending=step(realEnd,healthX)*step(healthX,max(_Cursor,_Target))*step(.00001,abs(_Cursor-_Target));
     // The final curled lip is decoration, while the long ribbon remains metered.
     float accent=1-step(_MeterRange.x,sourceX);
     rgb=lerp(_EmptyColor.rgb,rgb,max(step(healthX,realEnd)*step(.00001,realEnd),accent));
     rgb*=_Brightness;
     // The danger pulse belongs to settled health; transient solid yellow/green
     // remains readable, and its white flash / cursor are drawn above that pulse.
     float bandLight=_PendingInnerLight*(.30*exp(-crossSection*crossSection*5)+rim*.45);
     float3 pendingWhite=lerp(pendingColor,float3(1,.99,.90),.68);
     rgb=lerp(rgb,lerp(pendingColor,pendingWhite,saturate(bandLight)),pending);
     rgb=lerp(rgb,_FlashColor.rgb,_Flash*flashRegion);
     float aa=max(fwidth(x)*.5,.0001);
     tick=(1-smoothstep(max(0,_TickWidth*.5-aa),_TickWidth*.5+aa,tickDistance))*_TickVisible;
     float innerGlow=exp(-tickDistance/max(.001,_TickWidth*2.5))*saturate(_TickGlow*.22)*_TickVisible;
     rgb=lerp(rgb,_FlashColor.rgb,innerGlow);
     // The optical marker below is the only core; do not draw a second bar-clipped tick.
    }
    fixed3 loadingWhite=fixed3(1,.985,.945);
    #ifndef UNITY_COLORSPACE_GAMMA
    loadingWhite=GammaToLinearSpace(loadingWhite);
    #endif
    float trail=exp(-max(0,_Load-revealX)*8);
    float3 loadPigment=loadingWhite*(.88+.12*max(trail,_Identity));
    loadPigment=lerp(loadPigment,loadingWhite,saturate(head*.9+rim*.2));
    rgb=lerp(rgb,loadPigment,saturate(_IntroBlend+_ResolvePulse*.35));
    float core=tex.a*reveal;
    float dilated=tex.a;
    // Bounded 8-tap dilation only for the two bars / a brief loading overlay.
    if(_TickGlow+_Flash+_IntroBlend+interval>.001) {
     float2 d=float2(_GlowRadiusX,_GlowRadiusY);
     if(_HasDistance>.5){
      float encoded=tex2D(_DistanceTex,_DistanceUV.xy+uv*_DistanceUV.zw).r;
      float distance=encoded*_DistanceScale;
      // The baked field has finite reach. Fade at its end instead of clamping
      // to a nonzero opacity across the padded UI rectangle.
      dilated=exp(-2.8*pow(distance/max(.1,_GlowWorld),2))*(1-smoothstep(.72,1,encoded));
     }else dilated=softAlpha(uv,d);
    }
    // Separate, shape-aware optical marker. Its core may protrude while the
    // health fill continues to use the unchanged master silhouette.
    float tickSource=lerp(_MeterRange.x+tickPosition*(_MeterRange.y-_MeterRange.x),1-(_MeterRange.x+tickPosition*(_MeterRange.y-_MeterRange.x)),_Flip);
    float2 tickProfile=tex2D(_CurveTex,float2(saturate(tickSource),.5)).rg;
    float tickCenter=lerp(centerY,(tickProfile.x+tickProfile.y)*.5,_HasProfile);
    float tickHalf=lerp(.24,max(.025,(tickProfile.y-tickProfile.x)*.5),_HasProfile);
    float cy=(uv.y-tickCenter)/tickHalf;
    float extent=lerp(.90,1.65,_TickActivity);
    float endTaper=pow(saturate(1-pow(cy/extent,2)),.65);
    float arc=_Cursor+lerp(.003,.012,_TickActivity)*(1-pow(cy/extent,2));
    float d=abs(x-arc);
    float markerWidth=_TickWidth*lerp(.65,1.05,_TickActivity)*endTaper;
    float marker=(1-smoothstep(markerWidth*.25,markerWidth*.65+fwidth(x),d))*endTaper*_TickVisible;
    marker*=lerp(tex.a,1,_TickActivity);
    float nearGlow=exp(-pow(d/max(.001,_GlowRadiusX*.42),2)-pow(cy/1.5,2))*_TickGlow*_TickVisible*.7;
    float farGlow=exp(-pow(d/max(.001,_GlowRadiusX*1.1),2)-pow(cy/2.0,2))*_TickGlow*_TickVisible*.13;
    float flashHalo=_Flash*_FlashGlow*flashRegion;
    float introHalo=_IntroBlend*(_IntroGlow*(.30+.70*max(trail,_Identity))+head*_HeadGlow)+_ResolvePulse*.45;
    float pw=max(.001,_GlowRadiusX*.65);
    float pendingSoft=smoothstep(min(_Cursor,_Target)-pw,min(_Cursor,_Target)+pw,healthX)*
                      (1-smoothstep(max(_Cursor,_Target)-pw,max(_Cursor,_Target)+pw,healthX));
    float pendingHalo=0;
    float glow=(1-exp(-dilated*(flashHalo+introHalo+pendingHalo)*1.5))*max(reveal,head);
    float markerGlow=saturate(nearGlow+farGlow);
    glow=max(glow,markerGlow);
    fixed3 glowColor=lerp(_FlashColor.rgb,loadingWhite,_IntroBlend);
    glowColor=lerp(glowColor,lerp(pendingColor,float3(1,1,.92),.38),pendingHalo/max(.001,pendingHalo+nearGlow+flashHalo+introHalo));
    float alpha=saturate(core+glow*(1-core));
    rgb=(rgb*core+glowColor*glow*(1-core))/max(.0001,alpha);
    // A bright thin centre plus coloured near glow, rather than an opaque white band.
    float3 markerWhite=lerp(_FlashColor.rgb,loadingWhite,_LoadingTick);
    rgb=lerp(rgb,markerWhite,saturate(marker));
    alpha=saturate(alpha+marker*(1-alpha));
    fixed4 result=fixed4(rgb,alpha*_EntryVisible)*i.color;
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
