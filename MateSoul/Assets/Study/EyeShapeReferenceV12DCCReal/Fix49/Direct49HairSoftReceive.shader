// Local VRM0 MToon port. Lighting formula follows UniVRM 0.131.3
// Packages/VRM/MToon/Shaders/MToonCore.cginc (MIT); no outline pass.
Shader "LocalStudyOnly/Direct49 Hair Soft Receive"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Color ("Base multiplier", Color) = (1,1,1,1)
        _ShadeTexture ("Shade", 2D) = "white" {}
        _ShadeColor ("Shade multiplier", Color) = (1,1,1,1)
        _BumpMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal scale", Float) = 1
        _ShadeShift ("Shade shift", Float) = 0
        _ShadeToony ("Shade toony", Range(0,1)) = .9
        _ReceiveShadowTexture ("Receive shadow", 2D) = "white" {}
        _ReceiveShadowRate ("Receive shadow rate", Float) = 1
        _ShadingGradeTexture ("Shading grade", 2D) = "white" {}
        _ShadingGradeRate ("Shading grade rate", Float) = 1
        _LightColorAttenuation ("Light color attenuation", Float) = 0
        _IndirectLightIntensity ("Indirect normal mix", Float) = .1
        _RimTexture ("Rim mask", 2D) = "white" {}
        _RimColor ("Rim", Color) = (0,0,0,1)
        _RimLightingMix ("Rim light mix", Float) = 0
        _RimFresnelPower ("Rim power", Float) = 100
        _RimLift ("Rim lift", Float) = .1
        _SphereAdd ("MatCap", 2D) = "black" {}
        _MatcapColor ("MatCap tint", Color) = (1,1,1,1)
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission color", Color) = (0,0,0,1)
        _FabricDetailMap ("Fabric detail", 2D) = "gray" {}
        _FabricDetailScale ("Detail tiling", Float) = 64
        _FabricAlbedoStrength ("Detail intensity", Float) = 0
        _Cutoff ("Cutoff", Range(0,1)) = .5
        [Enum(Opaque,0,Cutout,1,Transparent,2)] _BlendMode ("Blend mode", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _CullMode ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
        [HideInInspector] _OutlineWidthMode ("Outline off", Float) = 0
        [HideInInspector] _OutlineWidth ("Outline width", Float) = 0
        [Enum(Lit,0,Base,1,Shade,2,Normal,3,Emission,4,MatCap,5)] _DebugMode ("Channel", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST, _Color, _ShadeColor, _RimColor, _EmissionColor, _MatcapColor;
        float _BumpScale, _ShadeShift, _ShadeToony, _ReceiveShadowRate, _ShadingGradeRate;
        float _LightColorAttenuation, _IndirectLightIntensity, _RimLightingMix, _RimFresnelPower, _RimLift;
        float _Cutoff, _BlendMode, _DebugMode, _FabricDetailScale, _FabricAlbedoStrength;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_ShadeTexture); SAMPLER(sampler_ShadeTexture);
        TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
        TEXTURE2D(_ReceiveShadowTexture); SAMPLER(sampler_ReceiveShadowTexture);
        TEXTURE2D(_ShadingGradeTexture); SAMPLER(sampler_ShadingGradeTexture);
        TEXTURE2D(_RimTexture); SAMPLER(sampler_RimTexture);
        TEXTURE2D(_SphereAdd); SAMPLER(sampler_SphereAdd);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        TEXTURE2D(_FabricDetailMap); SAMPLER(sampler_FabricDetailMap);
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
        struct Varyings {
            float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1;
            float4 tangentWS:TEXCOORD2; float2 uv:TEXCOORD3; float4 shadowCoord:TEXCOORD4;
        };
        Varyings Vert(Attributes i) {
            Varyings o;
            VertexPositionInputs p=GetVertexPositionInputs(i.positionOS.xyz);
            o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(i.normalOS);
            o.tangentWS=float4(TransformObjectToWorldDir(i.tangentOS.xyz),i.tangentOS.w*GetOddNegativeScale());
            o.uv=TRANSFORM_TEX(i.uv,_MainTex);o.shadowCoord=GetShadowCoord(p);return o;
        }
        float3 AttenuateColor(float3 c) {
            return lerp(c,max(.0009765625,max(c.x,max(c.y,c.z))),_LightColorAttenuation);
        }
        float ToonRate(float ndotl,float attenuation,float grade) {
            float rate=((ndotl*.5+.5)*attenuation*grade)*2-1;
            float upper=lerp(1,_ShadeShift,_ShadeToony);
            float width=max(max(.0009765625,upper-_ShadeShift),fwidth(rate)*1.5);
            return saturate((rate-(upper+_ShadeShift)*.5)/width+.5);
        }
        float4 Frag(Varyings i):SV_Target {
            float4 base=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv)*_Color;
            float alpha=_BlendMode>1.5?base.a:1;
            if(_BlendMode>.5 && _BlendMode<1.5) clip(base.a-_Cutoff);
            if(_BlendMode>1.5) clip(alpha-.0009765625);
            float3 n=normalize(i.normalWS);
            float3 t=normalize(i.tangentWS.xyz);
            float3 b=cross(n,t)*i.tangentWS.w;
            float3 tn=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale);
            n=normalize(t*tn.x+b*tn.y+n*tn.z);
            float3 view=normalize(lerp(_WorldSpaceCameraPos-i.positionWS,UNITY_MATRIX_V[2].xyz,unity_OrthoParams.w));
            n*=step(0,dot(view,n))*2-1;
            float3 shade=SAMPLE_TEXTURE2D(_ShadeTexture,sampler_ShadeTexture,i.uv).rgb*_ShadeColor.rgb;
            float detail=1+(SAMPLE_TEXTURE2D(_FabricDetailMap,sampler_FabricDetailMap,i.uv*_FabricDetailScale).r-.5)*_FabricAlbedoStrength;
            base.rgb*=detail;shade*=detail;
            float grade=1-_ShadingGradeRate*(1-SAMPLE_TEXTURE2D(_ShadingGradeTexture,sampler_ShadingGradeTexture,i.uv).r);
            // Cascade selection is discrete: choose it per fragment, never interpolate
            // vertex-selected cascade coordinates across a triangle.
            float4 shadowCoord=TransformWorldToShadowCoord(i.positionWS);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            shadowCoord=i.shadowCoord;
            #endif
            Light light=GetMainLight(shadowCoord);
            float receive=_ReceiveShadowRate*SAMPLE_TEXTURE2D(_ReceiveShadowTexture,sampler_ReceiveShadowTexture,i.uv).r;
            float attenuation=lerp(1,light.shadowAttenuation,saturate(receive));
            float3 lighting=AttenuateColor(light.color*light.distanceAttenuation);
            float3 color=lerp(shade,base.rgb,ToonRate(dot(n,light.direction),attenuation,grade))*lighting;
            float3 toonGI=.5*(SampleSH(float3(0,1,0))+SampleSH(float3(0,-1,0)));
            float3 indirect=AttenuateColor(lerp(toonGI,SampleSH(n),_IndirectLightIntensity));
            color=min(color+indirect*base.rgb,base.rgb);
            #ifdef _ADDITIONAL_LIGHTS
            uint count=GetAdditionalLightsCount();
            for(uint l=0;l<count;l++) {
                Light extra=GetAdditionalLight(l,i.positionWS);
                float nl=dot(n,extra.direction);
                float3 energy=AttenuateColor(extra.color)*.5*(min(0,nl)+1)*extra.distanceAttenuation*extra.shadowAttenuation;
                if(_BlendMode>1.5) energy*=step(0,nl);
                color+=lerp(shade,base.rgb,ToonRate(nl,1,grade))*energy;
            }
            #endif
            float3 rim=pow(saturate(1-dot(n,view)+_RimLift),max(_RimFresnelPower,.0009765625))*_RimColor.rgb;
            color+=rim*SAMPLE_TEXTURE2D(_RimTexture,sampler_RimTexture,i.uv).rgb*lerp(1,lighting+indirect,_RimLightingMix);
            float3 up=normalize(UNITY_MATRIX_V[1].xyz);
            up=normalize(up-view*dot(view,up));
            float3 right=normalize(cross(view,up));
            float2 mc=float2(dot(right,n),dot(up,n))*.5+.5;
            float3 matcap=SAMPLE_TEXTURE2D(_SphereAdd,sampler_SphereAdd,mc).rgb*_MatcapColor.rgb;
            float3 emission=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,i.uv).rgb*_EmissionColor.rgb;
            color+=matcap+emission;
            if(_DebugMode>.5 && _DebugMode<1.5) color=base.rgb;
            if(_DebugMode>1.5 && _DebugMode<2.5) color=shade;
            if(_DebugMode>2.5 && _DebugMode<3.5) color=n*.5+.5;
            if(_DebugMode>3.5 && _DebugMode<4.5) color=emission;
            if(_DebugMode>4.5) color=matcap;
            return float4(color,alpha);
        }
        float4 DepthFrag(Varyings i):SV_Target {
            if(_BlendMode>.5) clip(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a*_Color.a-_Cutoff);
            return 0;
        }
        ENDHLSL
        Pass {
            Name "Forward" Tags {"LightMode"="UniversalForward"}
            Cull [_CullMode] Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            ENDHLSL
        }
        Pass {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
            Cull [_CullMode] ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            float3 _LightDirection;
            Varyings ShadowVert(Attributes i) {
                Varyings o=Vert(i);
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,_LightDirection));
                #if UNITY_REVERSED_Z
                o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #else
                o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            ENDHLSL
        }
        Pass {
            Name "DepthOnly" Tags {"LightMode"="DepthOnly"}
            Cull [_CullMode] ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            ENDHLSL
        }
    }
}
