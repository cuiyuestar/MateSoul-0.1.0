Shader "Hidden/MateSoul/Reality Environment Depth Composite"
{
    Properties
    {
        _EnvironmentDepthBias ("Environment Depth Bias", Range(-0.1, 0.1)) = 0.01
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Reality Environment Occlusion"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Zero SrcAlpha
            ColorMask A

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRendering.hlsl"

            TEXTURE2D_ARRAY(_EnvironmentDepthTexture);
            SAMPLER(sampler_EnvironmentDepthTexture);
            float4 _EnvironmentDepthTexture_TexelSize;

            TEXTURE2D_ARRAY(_PreprocessedEnvironmentDepthTexture);
            SAMPLER(sampler_PreprocessedEnvironmentDepthTexture);
            float4 _PreprocessedEnvironmentDepthTexture_TexelSize;

            float SampleEnvironmentDepth(float2 uv)
            {
                return SAMPLE_TEXTURE2D_ARRAY(
                    _EnvironmentDepthTexture,
                    sampler_EnvironmentDepthTexture,
                    uv,
                    unity_StereoEyeIndex).r;
            }

            float4 SamplePreprocessedDepth(float2 uv, float slice)
            {
                return SAMPLE_TEXTURE2D_ARRAY(
                    _PreprocessedEnvironmentDepthTexture,
                    sampler_PreprocessedEnvironmentDepthTexture,
                    uv,
                    slice);
            }

            #define ENVIRONMENT_DEPTH_CONVERT_OBJECT_TO_WORLD(positionOS) \
                TransformObjectToWorld(positionOS.xyz)
            #include "Packages/com.bytedance.pico.xr/Assets/Resources/Shader/EnvironmentOcclusion.cginc"

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float _EnvironmentDepthBias;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(uv);

                #if UNITY_REVERSED_Z
                    if (rawDepth <= 0.000001)
                        return half4(0, 0, 0, 1);
                #else
                    if (rawDepth >= 0.999999)
                        return half4(0, 0, 0, 1);
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif

                #if defined(SUPPORTS_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
                    UNITY_BRANCH if (_FOVEATED_RENDERING_NON_UNIFORM_RASTER)
                        uv = RemapFoveatedRenderingNonUniformToLinear(uv);
                #endif

                float3 positionWS =
                    ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float occlusion = CalculateEnvironmentDepthOcclusion(
                    positionWS,
                    _EnvironmentDepthBias);
                return half4(0, 0, 0, saturate(occlusion));
            }
            ENDHLSL
        }
    }
}
