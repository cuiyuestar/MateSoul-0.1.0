Shader "MateSoul/Lightweight Fluid"
{
    Properties { _BaseColor ("Water", Color) = (0.5,0.85,1,0.6) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Input { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 positionCS : SV_POSITION; float4 color : COLOR; UNITY_VERTEX_OUTPUT_STEREO };
            Output Vert(Input value)
            {
                Output result;
                UNITY_SETUP_INSTANCE_ID(value);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(result);
                result.positionCS = TransformObjectToHClip(value.positionOS.xyz);
                result.color = value.color;
                return result;
            }
            half4 Frag(Output value) : SV_Target { return value.color; }
            ENDHLSL
        }
    }
}
