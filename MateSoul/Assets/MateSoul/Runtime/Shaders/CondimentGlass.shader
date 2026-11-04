Shader "MateSoul/Condiment Glass"
{
    Properties
    {
        _BaseColor("Glass Tint", Color) = (0.88,0.96,0.98,0.065)
        _EdgeOpacity("Edge Opacity", Range(0,1)) = 0.48
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _EdgeOpacity;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normal = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half edge = 1 - saturate(abs(dot(normal, view)));
                edge *= edge;
                Light light = GetMainLight();
                half specular = pow(saturate(dot(normal, normalize(light.direction + view))), 56);
                half3 color = _BaseColor.rgb * (0.65 + 0.35 * saturate(dot(normal, light.direction)));
                color += light.color * (specular * 0.7 + edge * 0.2);
                return half4(color, saturate(_BaseColor.a + edge * _EdgeOpacity + specular * 0.4));
            }
            ENDHLSL
        }
    }
}
