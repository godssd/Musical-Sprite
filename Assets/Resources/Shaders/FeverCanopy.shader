Shader "MusicalSprite/FeverCanopy" {
 Properties {
  [PerRendererData] _MainTex("Canopy",2D)="white" {}
  _LoopSeconds("Loop seconds",Float)=6
  _Phase("Phase",Float)=0
  _Sway("Leaf sway",Float)=.003
  _FlowerPulse("Flower light",Float)=.07
  _ReviewTime("Review time (-1 uses game time)",Float)=-1
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="False"}
  Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
  Pass {
   Tags {"LightMode"="SRPDefaultUnlit"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
   CBUFFER_START(UnityPerMaterial)
   float _LoopSeconds,_Phase,_Sway,_FlowerPulse,_ReviewTime;
   CBUFFER_END
   struct A {float3 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
   V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS);o.uv=i.uv;o.color=i.color;return o;}
   half4 frag(V i):SV_Target {
    float time=_ReviewTime>=0?_ReviewTime:_Time.y;
    float phase=time*6.2831853/max(2,_LoopSeconds)+_Phase;
    float2 uv=i.uv;
    // Roots stay pinned; only leaves/flower boughs sway. Two harmonic motions close the loop.
    float pin=smoothstep(.12,.75,uv.y)*pow(saturate(sin(uv.x*3.1415926)),.65);
    uv.x+=(sin(phase+uv.x*6.2831853)*.7+sin(phase*2+uv.y*8.0)*.3)*_Sway*pin;
    uv.y+=sin(phase*2+uv.x*12.56637)*_Sway*.35*pin;
    half4 c=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv)*i.color;
    // Pulse the bright flower lamps, not every orange leaf. Upper foliage stays
    // quieter behind the HUD while the two scenery layers move independently.
    float lamps=smoothstep(.34,.70,c.g)*smoothstep(.05,.38,c.r-c.b);
    c.rgb*=1+lamps*_FlowerPulse*(.5+.5*sin(phase*2+uv.x*12.56637));
    c.rgb*=1-.16*smoothstep(.65,.97,uv.y);
    return c;
   }
   ENDHLSL
  }
 }
}
