Shader "MusicalSprite/UI/UnifiedLoadingV7" {
 Properties {
  [PerRendererData] _MainTex("Unified alpha / near / far",2D)="black"{}
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
   struct appdata{float4 vertex:POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;};
   struct v2f{float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;fixed4 color:COLOR;float4 world:TEXCOORD1;};
   sampler2D _MainTex;float4 _ClipRect,_MaskDimensions,_Head,_Tangent,_White;
   float _Side,_TeamWidth,_Travel,_Visible,_NearStrength,_FarStrength;
   v2f vert(appdata v){v2f o;o.world=v.vertex;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   fixed4 frag(v2f i):SV_Target{
    float2 p=float2(i.uv.x,1-i.uv.y)*_MaskDimensions.xy-_MaskDimensions.zw;
    float axis=lerp(p.x,_TeamWidth-p.x,_Side);
    float revealed=1-smoothstep(_Travel-.35,_Travel+.35,axis);
    float3 mask=tex2D(_MainTex,i.uv).rgb;
    float core=mask.r*revealed;
    float halo=(mask.g*_NearStrength+mask.b*_FarStrength)*revealed;
    float2 q=p-_Head.xy;float along=dot(q,_Tangent.xy),normal=dot(q,_Tangent.zw);
    float cap=max(2,_Head.z+7);
    float segmentDistance=length(float2(along,max(0,abs(normal)-cap)));
    float streak=exp(-pow(along/.7,2))*pow(saturate(1-pow(normal/cap,2)),.55);
    float hotspot=exp(-.5*(pow(along/1.5,2)+pow(normal/2.2,2)));
    float headHalo=exp(-.5*(pow(along/10,2)+pow(normal/(cap*.95),2)))*.85;
    float headCore=saturate(streak*.95+hotspot)*_Head.w;
    float coverage=max(core,headCore);
    float3 glow=float3(1,.83,.53)*halo+float3(1,.91,.72)*headHalo*_Head.w;
    float visibility=_Visible*i.color.a;
    #ifdef UNITY_UI_CLIP_RECT
    visibility*=UnityGet2DClipping(i.world.xy,_ClipRect);
    #endif
    // Premultiplied coverage plus independent emitted light. An alpha-zero
    // halo adds light instead of blending the background towards a dull colour.
    fixed4 result=fixed4((_White.rgb*coverage+glow)*i.color.rgb*visibility,coverage*visibility);
    return result;
   }
   ENDCG
  }
 }
}
