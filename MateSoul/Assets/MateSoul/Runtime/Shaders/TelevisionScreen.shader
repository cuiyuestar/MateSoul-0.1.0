Shader "MateSoul/Television Screen"
{
    Properties
    {
        _BaseMap ("Off Screen", 2D) = "black" {}
        _VideoTex ("Video", 2D) = "black" {}
        _ScreenPower ("Power", Range(0,1)) = 0
        _VideoReady ("Video Ready", Range(0,1)) = 0
        _IdleColor ("No Signal Color", Color) = (.035,.045,.07,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "TelevisionScreen"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_VideoTex); SAMPLER(sampler_VideoTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _IdleColor;
                half _ScreenPower;
                half _VideoReady;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 off = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * .15h;
                half3 video = SAMPLE_TEXTURE2D(_VideoTex, sampler_VideoTex, input.uv).rgb;
                half3 on = lerp(_IdleColor.rgb, video, _VideoReady);
                return half4(lerp(off, on, _ScreenPower), 1);
            }
            ENDHLSL
        }
    }
}
