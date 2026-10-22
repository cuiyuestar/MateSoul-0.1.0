Shader "MateSoul/Carrot Interior"
{
    Properties
    {
        _BaseMap("Interior Base Color", 2D) = "white" {}
        _CoreMask("Core Mask", 2D) = "black" {}
        _CrossHeight("Cross Cut Detail", 2D) = "gray" {}
        _LongHeight("Long Cut Detail", 2D) = "gray" {}
        _CoreTint("Core Tint", Color) = (1.04, 0.96, 0.78, 1)
        _DetailStrength("Detail Strength", Range(0, 0.4)) = 0.16
        _Smoothness("Fresh Cut Smoothness", Range(0, 1)) = 0.38
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_CoreMask);
            SAMPLER(sampler_CoreMask);
            TEXTURE2D(_CrossHeight);
            SAMPLER(sampler_CrossHeight);
            TEXTURE2D(_LongHeight);
            SAMPLER(sampler_LongHeight);

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreTint;
                float _DetailStrength;
                float _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 crossUv : TEXCOORD0;
                float2 longUv : TEXCOORD1;
                float2 cutData : TEXCOORD2;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 crossUv : TEXCOORD2;
                float2 longUv : TEXCOORD3;
                half alignment : TEXCOORD4;
                half fogFactor : TEXCOORD5;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.crossUv = input.crossUv;
                output.longUv = input.longUv;
                output.alignment = saturate(input.cutData.x);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirection = SafeNormalize(
                    GetWorldSpaceViewDir(input.positionWS));
                half4 baseSample = SAMPLE_TEXTURE2D(
                    _BaseMap, sampler_BaseMap, input.crossUv);
                half core = SAMPLE_TEXTURE2D(
                    _CoreMask, sampler_CoreMask, input.crossUv).r;
                half crossDetail = SAMPLE_TEXTURE2D(
                    _CrossHeight, sampler_CrossHeight, input.crossUv).r - 0.5h;
                half longDetail = SAMPLE_TEXTURE2D(
                    _LongHeight, sampler_LongHeight,
                    input.longUv * half2(1.6h, 3.2h)).r - 0.5h;
                half crossWeight = smoothstep(0.5h, 0.94h, input.alignment);
                half detail = lerp(longDetail, crossDetail, crossWeight);
                half3 albedo = baseSample.rgb *
                    lerp(half3(1, 1, 1), _CoreTint.rgb, core * 0.18h);
                albedo *= 1.0h + detail * _DetailStrength;

                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 halfDirection = SafeNormalize(
                    mainLight.direction + viewDirection);
                half specular = pow(saturate(dot(normalWS, halfDirection)),
                    lerp(12.0h, 64.0h, _Smoothness)) *
                    lerp(0.015h, 0.11h, _Smoothness);
                half3 color = albedo *
                    (ambient + mainLight.color * diffuse * 0.76h) +
                    mainLight.color * specular;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
