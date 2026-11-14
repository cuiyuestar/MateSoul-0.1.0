Shader "LocalStudyOnly/DirectEyelash"
{
 Properties { _Color("Color",Color)=(0.015,0.01,0.025,1) [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest",Float)=4 }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest+50" "RenderType"="Opaque" }
  Pass { Name "Forward" Tags {"LightMode"="UniversalForward"} Cull Off ZWrite On ZTest [_ZTest]
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;};float4 _Color;
   V vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);return o;}
   float4 frag(V i):SV_Target{return _Color;}
   ENDHLSL
  }
 }
}
