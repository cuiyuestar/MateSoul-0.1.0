Shader "LocalStudyOnly/Direct51 Hair Strand Lines"
{
 Properties { _LineColor("Line Color",Color)=(0.016,0.006,0.035,1) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+5" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Cull Off ZTest LEqual ZWrite Off Offset -1,-1
   Blend SrcAlpha OneMinusSrcAlpha
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _LineColor;
   CBUFFER_END
   struct A { float4 pos:POSITION; float4 color:COLOR; };
   struct V { float4 pos:SV_POSITION; float4 color:COLOR; };
   V vert(A i) { V o; o.pos=TransformObjectToHClip(i.pos.xyz);o.color=i.color;return o; }
   half4 frag(V i):SV_Target { return half4(_LineColor.rgb,_LineColor.a*i.color.a); }
   ENDHLSL
  }
 }
}
