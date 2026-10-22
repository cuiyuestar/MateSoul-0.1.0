Shader "MateSoul/Carrot Exterior"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.94, 0.24, 0.025, 1)
        _DarkColor("Groove Color", Color) = (0.56, 0.11, 0.012, 1)
        _Smoothness("Smoothness", Range(0, 1)) = 0.24
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

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _DarkColor;
                float _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(
                    input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalWS = normalize(input.normalWS);
                half3 viewDirection = SafeNormalize(
                    GetWorldSpaceViewDir(input.positionWS));
                float angle = atan2(input.positionOS.z, input.positionOS.x);
                float largeBand = sin(input.positionOS.y * 355.0 +
                    sin(angle * 5.0) * 0.75);
                float fineBand = sin(input.positionOS.y * 920.0 +
                    angle * 2.0);
                half groove = smoothstep(0.78, 0.99, largeBand) * 0.14h +
                              smoothstep(0.91, 1.0, fineBand) * 0.035h;
                half variation = sin(angle * 7.0 +
                    input.positionOS.y * 67.0) * 0.018h;
                half3 albedo = lerp(_BaseColor.rgb, _DarkColor.rgb, groove);
                albedo *= 1.0h + variation;

                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 halfDirection = SafeNormalize(
                    mainLight.direction + viewDirection);
                half specular = pow(saturate(dot(normalWS, halfDirection)),
                    lerp(10.0h, 58.0h, _Smoothness)) *
                    lerp(0.012h, 0.075h, _Smoothness);
                half3 color = albedo *
                    (ambient + mainLight.color * diffuse * 0.78h) +
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
