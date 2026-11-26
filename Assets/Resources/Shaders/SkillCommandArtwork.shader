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
   sampler2D _MainTex,_PathTex;float4 _ClipRect,_Press,_Release,_BrightHue,_GlowHue,_ShadowHue;float2 _GlowUV;
   float _Reset,_IsIcon,_LocalPress;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   float Alpha(float2 uv){return tex2D(_MainTex,uv).a;}
   fixed4 frag(v2f i):SV_Target {
    float4 tex=tex2D(_MainTex,i.uv);
    float3 regions=exp(-pow((i.uv.y-float3(.818,.500,.206))/.065,2));
    float shortArc=exp(-pow((i.uv.x-.66)/.16,2));
    float press=_IsIcon>.5?_LocalPress*.23:dot(_Press.xyz,regions)*shortArc;
    // Three simultaneous contour sweeps. Progress is shared; original PNG alpha
    // supplies the real asymmetric silhouette and its open gaps.
    float2 scale=float2(724,2172);
    float2 p0=(i.uv-float2(.504,.818))*scale;
    float2 p1=(i.uv-float2(.482,.500))*scale;
    float2 p2=(i.uv-float2(.449,.206))*scale;
    float d0=abs(length(p0)-225),d1=abs(length(p1)-230),d2=abs(length(p2)-240);
    float2 local=p0;float distance=d0;
    if(d1<distance){local=p1;distance=d1;}
    if(d2<distance){local=p2;distance=d2;}
    float rim=1-smoothstep(65,105,distance);
    float phase=frac((1.5707963-atan2(local.y,local.x))/6.2831853+1);
    float delta=abs(phase-frac(_Release.y));delta=min(delta,1-delta);
    float head=exp(-pow(delta/.055,2))*rim;
    float tail=exp(-frac(_Release.y-phase+1)/.15)*rim;
    float finishZone=_IsIcon>.5?.14:rim;
    float finish=_Release.w*finishZone;
    float light=press+_Release.x*(_IsIcon>.5?.13:(head*.72+tail*.18))*_Release.z+finish*.85;
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
    // A restrained pigment shadow, with its own hue; never a gray wash or dark disk.
    float shade=(1-smoothstep(.05,.85,i.uv.y))*.055;
    base=lerp(base,base*_ShadowHue.rgb,shade+_Reset*.16)*(1-_Reset*.18);
    float response=1-exp(-light*1.5);
    base=lerp(base,_BrightHue.rgb,response*(_IsIcon>.5?.28:.60));
    float3 core=_BrightHue.rgb;
    #ifndef UNITY_COLORSPACE_GAMMA
    core=LinearToGammaSpace(core);
    #endif
    core=lerp(core,float3(1,.98,.89),.64);
    #ifndef UNITY_COLORSPACE_GAMMA
    core=GammaToLinearSpace(core);
    #endif
    base=lerp(base,core,saturate(head*_Release.x*(_IsIcon>.5?0:.72)+finish*.62));
    // Independent emitted light: not a dark alpha-blended outline and no filled disks.
    float3 glow=lerp(_GlowHue.rgb,_BrightHue.rgb,saturate(halo*3))*halo*response*1.15*(1-tex.a*.90);
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
