Shader "MusicalSprite/UI/SkillCommandArtwork" {
 Properties {
  [PerRendererData] _MainTex("Artwork",2D)="white"{}
  _StencilComp("Stencil comparison",Float)=8 _Stencil("Stencil",Float)=0
  _StencilOp("Stencil operation",Float)=0 _StencilWriteMask("Stencil write",Float)=255
  _StencilReadMask("Stencil read",Float)=255 _ColorMask("Color mask",Float)=15
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"}
  Stencil {Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off ZWrite Off ZTest [unity_GUIZTestMode] Blend One OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct appdata {float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   struct v2f {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float4 world:TEXCOORD1;};
   sampler2D _MainTex;float4 _ClipRect,_Press,_Release;float2 _GlowUV;
   float _Reset,_IsIcon,_LocalPress;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   float Alpha(float2 uv){return tex2D(_MainTex,uv).a;}
   fixed4 frag(v2f i):SV_Target {
    float4 tex=tex2D(_MainTex,i.uv);
    float press=_IsIcon>.5?_LocalPress:dot(_Press.xyz,exp(-pow((i.uv.y-float3(.818,.500,.206))/.105,2)));
    float head=exp(-pow((1-i.uv.y-_Release.y)/.075,2));
    float light=press+_Release.x*(_IsIcon>.5?1.1:(.55+head*1.25))*_Release.z;
    float halo=0;
    // A filled Gaussian kernel, rather than displaced contour copies which
    // produce hard concentric outlines. Idle artwork skips the glow samples.
    if(light>.001){
     float weightSum=0;
     [unroll] for(int x=-3;x<=3;x++){
      [unroll] for(int y=-3;y<=3;y++){
       float weight=exp(-(x*x+y*y)/5.0);
       halo+=Alpha(i.uv+float2(x,y)*_GlowUV*2.6)*weight;weightSum+=weight;
      }
     }
     halo/=weightSum;
    }
    float3 base=tex.rgb*i.color.rgb;
    base=lerp(base,dot(base,float3(.299,.587,.114)).xxx*.8,_Reset*.75);
    float3 white=float3(1,.95,.80);
    base=lerp(base,white,saturate(light*.75));
    // Independent emitted light: not a dark alpha-blended outline and no filled disks.
    float3 glow=float3(1,.73,.43)*halo*light*1.65;
    float visibility=i.color.a;
    #ifdef UNITY_UI_CLIP_RECT
    visibility*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    return fixed4((base*tex.a+glow)*visibility,tex.a*visibility);
   }
   ENDCG
  }
 }
}
