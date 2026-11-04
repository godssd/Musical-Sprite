Shader "MusicalSprite/UI/RibbonOpticsV7" {
 Properties {
  [PerRendererData] _MainTex("UI",2D)="white"{}
  _StencilComp("Stencil comparison",Float)=8 _Stencil("Stencil",Float)=0
  _StencilOp("Stencil operation",Float)=0 _StencilWriteMask("Stencil write",Float)=255
  _StencilReadMask("Stencil read",Float)=255 _ColorMask("Color mask",Float)=15
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend One One ColorMask [_ColorMask]
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
   sampler2D _CurveTex;float4 _ClipRect,_BarSize,_CursorBoundary,_LowBoundary,_HighBoundary,_CoreColor,_HaloColor,_PendingColor;
   float4 _FlashBoundary0,_FlashBoundary1;
   float _SideSign,_Bow,_Motion,_Visible,_Visibility,_CoreWidth,_NearRadius,_FarRadius,_NearStrength,_FarStrength,_Overflow,_Interval,_EdgeStrength,_Flash;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   fixed4 frag(v2f i):SV_Target{
    float2 p=i.uv*_BarSize.xy;
    // Distance to a finite luminous arc; no extrapolated parabolic lobes.
    float extent=max(1,_CursorBoundary.z+lerp(-1,_Overflow,_Motion));
    float y=clamp(p.y,_CursorBoundary.y-extent,_CursorBoundary.y+extent);
    float x=RibbonBoundaryX(y,_CursorBoundary,_SideSign,_Bow);
    float distance=length(p-float2(x,y));
    float taper=pow(saturate(1-pow((y-_CursorBoundary.y)/extent,2)),.35);
    float aa=max(.35,fwidth(p.x));
    float core=(1-smoothstep(_CoreWidth*.5,_CoreWidth*.5+aa,distance))*taper;
    float nearGlow=exp(-.5*pow(distance/max(.3,_NearRadius),2))*_NearStrength;
    float farGlow=exp(-.5*pow(distance/max(.5,_FarRadius),2))*_FarStrength;
    float3 light=(_CoreColor.rgb*core*1.35+_HaloColor.rgb*(nearGlow+farGlow))*_Visible;
    float2 section=tex2D(_CurveTex,float2(saturate(i.uv.x),.5)).rg*_BarSize.y;
    float edgeDistance=min(abs(p.y-section.x),abs(p.y-section.y));
    float lo=RibbonProgressSide(p.x,p.y,_LowBoundary,_SideSign,_Bow);
    float hi=RibbonProgressSide(p.x,p.y,_HighBoundary,_SideSign,_Bow);
    float interval=smoothstep(-1,1,lo)*(1-smoothstep(-1,1,hi))*_Interval;
    // One thin coloured edge, not a whitened centre plus a thick band bloom.
    light+=_PendingColor.rgb*exp(-.5*pow(edgeDistance/1.15,2))*interval*_EdgeStrength;
    float f0=RibbonProgressSide(p.x,p.y,_FlashBoundary0,_SideSign,_Bow);
    float f1=RibbonProgressSide(p.x,p.y,_FlashBoundary1,_SideSign,_Bow);
    light+=_CoreColor.rgb*exp(-.5*pow(edgeDistance/2.2,2))*smoothstep(-1,1,f0)*(1-smoothstep(-1,1,f1))*_Flash*.32;
    light*=_Visibility*i.color.a;
    #ifdef UNITY_UI_CLIP_RECT
    light*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    return float4(light,0);
   }
   ENDCG
  }
 }
}
