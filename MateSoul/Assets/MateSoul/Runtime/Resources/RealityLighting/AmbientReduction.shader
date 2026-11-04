Shader "Hidden/MateSoul/Reality Ambient Reduction"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURECUBE(_EnvironmentCube);
            SAMPLER(sampler_EnvironmentCube);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 sum = 0;
                float valid = 0;
                // Uniform whole-sphere sampling; no head-relative or dominant-light direction is inferred.
                [unroll]
                for (int index = 0; index < 64; index++)
                {
                    float y = 1.0 - 2.0 * (index + .5) / 64.0;
                    float radius = sqrt(max(0, 1.0 - y * y));
                    float sine, cosine;
                    sincos(index * 2.39996323, sine, cosine);
                    float3 direction = float3(radius * cosine, y, radius * sine);
                    float4 value = SAMPLE_TEXTURECUBE_LOD(
                        _EnvironmentCube, sampler_EnvironmentCube, direction, 0);
                    bool finite = all(value == value) && all(abs(value) < 65504.0);
                    bool populated = value.a > .5 || any(value.rgb > .000001);
                    if (finite && populated)
                    {
                        sum += clamp(value.rgb, 0, 4);
                        valid += 1;
                    }
                }
                return float4(sum / max(valid, 1), valid / 64.0);
            }
            ENDHLSL
        }
    }
}
