Shader "MateSoul/Character Visual v003"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _ShadeMap("Painted Shade Color", 2D) = "white" {}
        _ShadeMapStrength("Painted Shade Strength", Range(0, 1)) = 0
        _SurfaceNormalMap("Surface Normal (RGB linear)", 2D) = "bump" {}
        _SurfaceNormalStrength("Surface Normal Strength", Range(0,1)) = 0
        _FaceMode("Authored Face Lighting", Range(0,1)) = 0
        _SkinMode("Soft Skin Lighting", Range(0,1)) = 0
        _FaceSDF("Face Left/Right SDF", 2D) = "gray" {}
        _FaceControl("Face Skin / Lip Wetness / Blush", 2D) = "black" {}
        _FaceRight("Head Right", Vector) = (1,0,0,0)
        _FaceForward("Head Forward", Vector) = (0,0,1,0)
        _EmissionMap("Eye Highlight / Emission", 2D) = "black" {}
        _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        [Enum(Off,0,Base,1,Shade,2,NdotL,3,Normal,4,Tangent,5,HairMask,6,Indirect,7,Shadow,8)]
        _QualityDebug("Quality Debug", Float) = 0
        _ShadowColor("Shadow Tint", Color) = (0.65, 0.55, 0.72, 1)
        _ShadowThreshold("Shadow Threshold", Range(0, 1)) = 0.48
        _ShadowSoftness("Shadow Softness", Range(0.001, 0.25)) = 0.035
        _DiffuseWrap("Diffuse Wrap", Range(0, 1)) = 0
        _IndirectStrength("Indirect Strength", Range(0, 1)) = 0.2
        _ReceiveShadows("Realtime Shadow Strength", Range(0, 1)) = 0.7
        _UnlitBlend("Unlit Blend", Range(0, 1)) = 0
        _SpecularColor("Toon Specular Color", Color) = (1, 1, 1, 1)
        _SpecularIntensity("Toon Specular Intensity", Range(0, 2)) = 0
        _SpecularThreshold("Toon Specular Threshold", Range(0, 1)) = 0.82
        _SpecularSoftness("Toon Specular Softness", Range(0.001, 0.25)) = 0.04
        _RimColor("Rim Light Color", Color) = (1, 1, 1, 1)
        _RimIntensity("Rim Light Intensity", Range(0, 1)) = 0.08
        _RimPower("Rim Light Power", Range(1, 12)) = 5
        _FabricDetailMap("Fabric Detail (R Height, GB Normal, A Fiber)", 2D) = "gray" {}
        _FabricDetailScale("Fabric Detail Scale", Range(1, 16)) = 6
        _FabricAlbedoStrength("Fabric Albedo Strength", Range(0, 0.2)) = 0
        _FabricNormalStrength("Fabric Normal Strength", Range(0, 0.5)) = 0
        _FabricSheenColor("Fabric Sheen Color", Color) = (1, 1, 1, 1)
        _FabricSheenIntensity("Fabric Sheen Intensity", Range(0, 0.3)) = 0
        _FabricSheenPower("Fabric Sheen Power", Range(1, 12)) = 4
        [HideInInspector] _DebugUV("Debug UV", Float) = 0
        [ToggleUI] _HairMode("Strand Toon Hair", Float) = 0
        _HairMidColor("Hair Middle Tone", Color) = (0.72, 0.65, 0.82, 1)
        _HairMidThreshold("Hair Light Threshold", Range(0, 1)) = 0.62
        _HairRoughness("Hair Highlight Roughness", Range(0.1, 0.7)) = 0.1
        _HairAnisotropy("Hair Anisotropy", Range(0, 1)) = 0.72
        _HairShift("Hair Highlight Shift", Range(-0.5, 0.5)) = 0.08
        _HairHighlightColor("Hair Highlight Color", Color) = (0.64, 0.52, 0.8, 1)
        _HairHighlightIntensity("Hair Highlight Intensity", Range(0, 1)) = 0.16
        _HairHighlightMap("Hair Per-Strand Anime Highlights (UV0)", 2D) = "black" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVertex
            #pragma fragment ToonFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_FabricDetailMap);
            SAMPLER(sampler_FabricDetailMap);
            TEXTURE2D(_HairHighlightMap);
            SAMPLER(sampler_HairHighlightMap);
            TEXTURE2D(_ShadeMap);
            SAMPLER(sampler_ShadeMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);
            TEXTURE2D(_SurfaceNormalMap);
            SAMPLER(sampler_SurfaceNormalMap);
            TEXTURE2D(_FaceSDF);
            SAMPLER(sampler_FaceSDF);
            TEXTURE2D(_FaceControl);
            SAMPLER(sampler_FaceControl);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _ShadeMapStrength;
                half _SurfaceNormalStrength;
                half _FaceMode;
                half _SkinMode;
                half4 _FaceRight;
                half4 _FaceForward;
                half _QualityDebug;
                half4 _ShadowColor;
                half4 _SpecularColor;
                half4 _RimColor;
                half _ShadowThreshold;
                half _ShadowSoftness;
                half _DiffuseWrap;
                half _IndirectStrength;
                half _ReceiveShadows;
                half _UnlitBlend;
                half _SpecularIntensity;
                half _SpecularThreshold;
                half _SpecularSoftness;
                half _RimIntensity;
                half _RimPower;
                half4 _FabricSheenColor;
                half _FabricDetailScale;
                half _FabricAlbedoStrength;
                half _FabricNormalStrength;
                half _FabricSheenIntensity;
                half _FabricSheenPower;
                half _DebugUV;
                half _HairMode;
                half4 _HairMidColor;
                half _HairMidThreshold;
                half _HairRoughness;
                half _HairAnisotropy;
                half _HairShift;
                half4 _HairHighlightColor;
                half _HairHighlightIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half3 normalOS : NORMAL;
                half4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 hairUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half3 vertexSH : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                half4 tangentWS : TEXCOORD6;
                float2 hairUV : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings ToonVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs =
                    GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs =
                    GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(normalInputs.normalWS);
                output.tangentWS = half4(
                    TransformObjectToWorldDir(input.tangentOS.xyz),
                    input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.hairUV = input.hairUV;
                output.vertexSH = SampleSH(output.normalWS);
                output.shadowCoord = GetShadowCoord(positionInputs);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half3 EvaluateToonLight(
                Light light, half3 normalWS, half3 viewDirectionWS, half3 shadeTint)
            {
                half ndotl = saturate(
                    (dot(normalWS, light.direction) + _DiffuseWrap)
                    / (1.0h + _DiffuseWrap));
                half adaptiveSoftness = max(
                    _ShadowSoftness, fwidth(ndotl) * 0.8h);
                half band = smoothstep(
                    _ShadowThreshold - adaptiveSoftness,
                    _ShadowThreshold + adaptiveSoftness, ndotl);
                band = lerp(band,smoothstep(.05h,.85h,ndotl),_SkinMode);
                half attenuation = light.distanceAttenuation
                    * lerp(1.0h, light.shadowAttenuation, _ReceiveShadows);
                half3 diffuse = lerp(shadeTint, 1.0h.xxx,
                    saturate(band * attenuation)) * light.color;

                half3 halfDirection =
                    SafeNormalize(light.direction + viewDirectionWS);
                half ndoth = saturate(dot(normalWS, halfDirection));
                half specularBand = smoothstep(
                    _SpecularThreshold - _SpecularSoftness,
                    _SpecularThreshold + _SpecularSoftness, ndoth);
                return diffuse + _SpecularColor.rgb
                    * specularBand * _SpecularIntensity * attenuation;
            }

            half3 EvaluateHairLight(
                Light light, half3 normalWS, half3 tangentWS,
                half tangentSign, half3 viewWS, half3 albedo, half3 ribbons,
                half3 shadeTint)
            {
                half ndotl = saturate(
                    (dot(normalWS, light.direction) + _DiffuseWrap)
                    / (1.0h + _DiffuseWrap));
                half visibility = light.distanceAttenuation *
                    lerp(1.0h, light.shadowAttenuation, _ReceiveShadows);
                // Quantize the shadowed lighting signal into three broad tones.
                half signal = ndotl * visibility;
                half edge = max(_ShadowSoftness, fwidth(signal));
                half mid = smoothstep(_ShadowThreshold - edge,
                    _ShadowThreshold + edge, signal);
                half lit = smoothstep(_HairMidThreshold - edge,
                    _HairMidThreshold + edge, signal);
                half3 midTint = lerp(_HairMidColor.rgb,
                    lerp(shadeTint, 1.0h.xxx, .60h), _ShadeMapStrength);
                half3 tone = lerp(shadeTint, midTint, mid);
                tone = lerp(tone, 1.0h.xxx, lit);

                // UV V follows root-to-tip; the bitangent follows the skinned
                // strand as the hair moves. No world-space stripes or noise.
                half3 across = SafeNormalize(
                    tangentWS - normalWS * dot(tangentWS, normalWS));
                half3 strand = SafeNormalize(cross(normalWS, across) * tangentSign);
                strand = SafeNormalize(strand + normalWS * _HairShift);
                half3 halfWS = SafeNormalize(light.direction + viewWS);
                half alongH = dot(strand, halfWS);
                half acrossH = dot(across, halfWS);
                // Directional response changes intensity without blurring the
                // graphic band edges painted into the mask.
                half widthAlong = 0.40h + _HairRoughness;
                half widthAcross = lerp(widthAlong, 1.4h, _HairAnisotropy);
                half lobe = exp2(-2.0h * (
                    alongH * alongH / (widthAlong * widthAlong) +
                    acrossH * acrossH / (widthAcross * widthAcross)));
                // Each UV0 tile belongs to a major strand. R is its pointed
                // main block, G is the bright core, and B holds small accents.
                // The mask defines sharp anime shapes; the broad lobe only
                // changes their intensity as the strand turns.
                half lobeResponse = lerp(.86h, 1.0h, lobe);
                half mainBlock = ribbons.r * .72h;
                half brightCore = ribbons.g;
                half block = max(mainBlock, brightCore) * lobeResponse;
                half accents = ribbons.b * .88h * lobeResponse;
                half specVisibility = smoothstep(0.0h, .22h, ndotl) *
                    lerp(.72h, 1.0h, saturate(dot(normalWS, viewWS))) * visibility;
                half3 graphicHighlight =
                    _HairHighlightColor.rgb * _HairHighlightIntensity * block;
                half3 accentColor = lerp(
                    _HairHighlightColor.rgb, half3(.76h, .62h, .90h), .58h);
                graphicHighlight += accentColor * _HairHighlightIntensity *
                    accents * .62h;
                return albedo * tone * light.color +
                    graphicHighlight * specVisibility * light.color;
            }

            half4 ToonFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 baseSample =
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv)
                    * _BaseColor;
                if (_DebugUV > .5h)
                    return half4(frac(input.uv), 0, 1);
                half4 fabricDetail = SAMPLE_TEXTURE2D(
                    _FabricDetailMap, sampler_FabricDetailMap,
                    input.uv * _FabricDetailScale);
                baseSample.rgb *= 1.0h + (fabricDetail.r - .5h) *
                    (2.0h * _FabricAlbedoStrength);
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 tangentWS = SafeNormalize(input.tangentWS.xyz);
                half3 bitangentWS = SafeNormalize(
                    cross(normalWS, tangentWS) * input.tangentWS.w);
                half3 surfaceNormal = SAMPLE_TEXTURE2D(_SurfaceNormalMap,
                    sampler_SurfaceNormalMap,input.uv).rgb * 2.0h - 1.0h;
                normalWS = SafeNormalize(normalWS +
                    (tangentWS * surfaceNormal.x + bitangentWS * surfaceNormal.y) * _SurfaceNormalStrength);
                half2 fabricNormal = (fabricDetail.gb * 2.0h - 1.0h) *
                    _FabricNormalStrength;
                normalWS = SafeNormalize(normalWS +
                    tangentWS * fabricNormal.x +
                    bitangentWS * fabricNormal.y);
                half3 viewDirectionWS =
                    SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                Light mainLight = GetMainLight(input.shadowCoord);
                half3 shade = SAMPLE_TEXTURE2D(_ShadeMap, sampler_ShadeMap, input.uv).rgb
                    * _BaseColor.rgb;
                half3 shadeTint = lerp(_ShadowColor.rgb,
                    shade / max(baseSample.rgb, .001h.xxx), _ShadeMapStrength);
                half3 ribbons = SAMPLE_TEXTURE2D(
                    _HairHighlightMap, sampler_HairHighlightMap, input.uv).rgb;
                if (_QualityDebug > .5h)
                {
                    half3 debugColor = baseSample.rgb;
                    if (_QualityDebug > 1.5h) debugColor = baseSample.rgb * shadeTint;
                    if (_QualityDebug > 2.5h) debugColor = saturate(dot(normalWS, mainLight.direction)).xxx;
                    if (_QualityDebug > 3.5h) debugColor = normalWS * .5h + .5h;
                    if (_QualityDebug > 4.5h) debugColor = tangentWS * .5h + .5h;
                    if (_QualityDebug > 5.5h) debugColor = ribbons;
                    if (_QualityDebug > 6.5h) debugColor = max(input.vertexSH, 0.0h) * _IndirectStrength;
                    if (_QualityDebug > 7.5h) debugColor = mainLight.shadowAttenuation.xxx;
                    return half4(debugColor, 1);
                }
                if (_HairMode > 0.5h)
                {
                    half3 hair = EvaluateHairLight(
                        mainLight, normalWS, input.tangentWS.xyz,
                        input.tangentWS.w, viewDirectionWS, baseSample.rgb, ribbons, shadeTint);
                    hair += baseSample.rgb * max(input.vertexSH, 0.0h) * _IndirectStrength;
                    return half4(MixFog(hair, input.fogFactor), baseSample.a);
                }
                half3 lighting =
                    EvaluateToonLight(mainLight, normalWS, viewDirectionWS, shadeTint);
                half3 faceControl = SAMPLE_TEXTURE2D(_FaceControl,
                    sampler_FaceControl,input.uv).rgb;
                if (_FaceMode > .5h)
                {
                    half2 faceSDF = SAMPLE_TEXTURE2D(_FaceSDF,sampler_FaceSDF,input.uv).rg;
                    half side = dot(mainLight.direction, _FaceRight.xyz);
                    half forward = dot(mainLight.direction, _FaceForward.xyz);
                    half field = side >= 0 ? faceSDF.r : faceSDF.g;
                    half threshold = .5h - .5h * forward;
                    half band = smoothstep(threshold-.035h,threshold+.035h,field);
                    half visibility = lerp(1.0h,mainLight.shadowAttenuation,_ReceiveShadows);
                    half3 faceLight = lerp(shadeTint,1.0h.xxx,band*visibility)*mainLight.color;
                    lighting = lerp(lighting,faceLight,faceControl.r);
                }

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    lighting += EvaluateToonLight(
                        light, normalWS, viewDirectionWS, shadeTint) * 0.35h;
                }
                #endif

                half3 indirect = max(input.vertexSH, 0.0h) * _IndirectStrength;
                half3 shaded = baseSample.rgb * (lighting + indirect);
                shaded = lerp(shaded, baseSample.rgb, _UnlitBlend);
                half wet = pow(saturate(dot(normalWS,
                    SafeNormalize(mainLight.direction+viewDirectionWS))),48.0h);
                shaded += faceControl.g * _FaceMode * wet * .055h
                    * half3(1.0h,.72h,.81h);

                half rim = pow(
                    1.0h - saturate(dot(normalWS, viewDirectionWS)), _RimPower);
                shaded += _RimColor.rgb * rim * _RimIntensity;
                half fabricSheen = pow(
                    1.0h - saturate(dot(normalWS, viewDirectionWS)),
                    _FabricSheenPower);
                shaded += _FabricSheenColor.rgb * fabricSheen *
                    lerp(.72h, 1.0h, fabricDetail.a) *
                    _FabricSheenIntensity;
                shaded += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb
                    * _EmissionColor.rgb;
                shaded = MixFog(shaded, input.fogFactor);
                return half4(shaded, baseSample.a);
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack Off
}
