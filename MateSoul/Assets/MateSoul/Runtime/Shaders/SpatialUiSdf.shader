Shader "MateSoul/Spatial UI SDF"
{
    Properties
    {
        [HDR] _FaceColor ("Face Color", Color) = (1,1,1,1)
        _FaceDilate ("Face Dilate", Range(-1,1)) = 0
        [HDR] _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Thickness", Range(0,1)) = 0
        _OutlineSoftness ("Outline Softness", Range(0,1)) = 0
        [HDR] _UnderlayColor ("Border Color", Color) = (0,0,0,.5)
        _UnderlayOffsetX ("Border OffsetX", Range(-1,1)) = 0
        _UnderlayOffsetY ("Border OffsetY", Range(-1,1)) = 0
        _UnderlayDilate ("Border Dilate", Range(-1,1)) = 0
        _UnderlaySoftness ("Border Softness", Range(0,1)) = 0
        _WeightNormal ("Weight Normal", Float) = 0
        _WeightBold ("Weight Bold", Float) = .5
        _ShaderFlags ("Flags", Float) = 0
        _ScaleRatioA ("Scale RatioA", Float) = 1
        _ScaleRatioB ("Scale RatioB", Float) = 1
        _ScaleRatioC ("Scale RatioC", Float) = 1
        _MainTex ("Font Atlas", 2D) = "white" {}
        _TextureWidth ("Texture Width", Float) = 512
        _TextureHeight ("Texture Height", Float) = 512
        _GradientScale ("Gradient Scale", Float) = 5
        _ScaleX ("Scale X", Float) = 1
        _ScaleY ("Scale Y", Float) = 1
        _PerspectiveFilter ("Perspective Correction", Range(0,1)) = .875
        _Sharpness ("Sharpness", Range(-1,1)) = 0
        _VertexOffsetX ("Vertex OffsetX", Float) = 0
        _VertexOffsetY ("Vertex OffsetY", Float) = 0
        _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        _MaskSoftnessX ("Mask SoftnessX", Float) = 0
        _MaskSoftnessY ("Mask SoftnessY", Float) = 0
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _CullMode ("Cull Mode", Float) = 0
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull [_CullMode]
        ZWrite Off
        Lighting Off
        Fog { Mode Off }
        // 恢复正常深度测试：虚拟手/角色等更近的几何体必须能遮挡 HUD。
        ZTest LEqual
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma shader_feature __ OUTLINE_ON
            #pragma shader_feature __ UNDERLAY_ON UNDERLAY_INNER
            #pragma multi_compile_local __ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local __ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #include "Assets/TextMesh Pro/Shaders/TMPro_Properties.cginc"
            struct Attributes
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 sdf : TEXCOORD1;
            };
            struct Varyings
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
                float4 vertex : SV_POSITION;
                fixed4 faceColor : COLOR;
                fixed4 outlineColor : COLOR1;
                float4 uv : TEXCOORD0;
                half4 param : TEXCOORD1;
                half4 mask : TEXCOORD2;
                #if (UNDERLAY_ON | UNDERLAY_INNER)
                float4 underlayUV : TEXCOORD3;
                half2 underlayParam : TEXCOORD4;
                #endif
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_INITIALIZE_OUTPUT(Varyings, output);
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float bold = step(input.sdf.y, 0);
                float4 vertex = input.vertex;
                vertex.xy += float2(_VertexOffsetX, _VertexOffsetY);
                output.vertex = UnityObjectToClipPos(vertex);
                float2 pixelSize = output.vertex.w;
                pixelSize /= float2(_ScaleX, _ScaleY) *
                    abs(mul((float2x2)UNITY_MATRIX_P, _ScreenParams.xy));
                float scale = rsqrt(dot(pixelSize, pixelSize));
                scale *= abs(input.sdf.y) * _GradientScale * (_Sharpness + 1);
                if (UNITY_MATRIX_P[3][3] == 0)
                    scale = lerp(abs(scale) * (1 - _PerspectiveFilter), scale,
                        abs(dot(UnityObjectToWorldNormal(input.normal),
                            normalize(WorldSpaceViewDir(vertex)))));
                float weight = lerp(_WeightNormal, _WeightBold, bold) / 4;
                weight = (weight + _FaceDilate) * _ScaleRatioA * .5;
                float layerScale = scale;
                scale /= 1 + (_OutlineSoftness * _ScaleRatioA * scale);
                float bias = (.5 - weight) * scale - .5;
                float outline = _OutlineWidth * _ScaleRatioA * .5 * scale;
                float opacity = input.color.a;
                #if (UNDERLAY_ON | UNDERLAY_INNER)
                opacity = 1;
                #endif
                output.faceColor = fixed4(input.color.rgb, opacity) * _FaceColor;
                output.faceColor.rgb *= output.faceColor.a;
                output.outlineColor = _OutlineColor;
                output.outlineColor.a *= opacity;
                output.outlineColor.rgb *= output.outlineColor.a;
                output.outlineColor = lerp(output.faceColor, output.outlineColor,
                    sqrt(min(1, outline * 2)));
                #if (UNDERLAY_ON | UNDERLAY_INNER)
                layerScale /= 1 + ((_UnderlaySoftness * _ScaleRatioC) * layerScale);
                float layerBias = (.5 - weight) * layerScale - .5 -
                    ((_UnderlayDilate * _ScaleRatioC) * .5 * layerScale);
                float2 layerOffset = -float2(_UnderlayOffsetX, _UnderlayOffsetY) *
                    _ScaleRatioC * _GradientScale / float2(_TextureWidth, _TextureHeight);
                output.underlayUV = float4(input.uv + layerOffset, input.color.a, 0);
                output.underlayParam = half2(layerScale, layerBias);
                #endif
                float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                float2 maskUV = (vertex.xy - clampedRect.xy) /
                    (clampedRect.zw - clampedRect.xy);
                output.uv = float4(input.uv, maskUV);
                output.param = half4(scale, bias - outline, bias + outline, bias);
                output.mask = half4(vertex.xy * 2 - clampedRect.xy - clampedRect.zw,
                    .25 / (.25 * half2(_MaskSoftnessX, _MaskSoftnessY) + pixelSize));
                return output;
            }
            fixed4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half distance = tex2D(_MainTex, input.uv.xy).a * input.param.x;
                half4 color = input.faceColor * saturate(distance - input.param.w);
                #ifdef OUTLINE_ON
                color = lerp(input.outlineColor, input.faceColor,
                    saturate(distance - input.param.z));
                color *= saturate(distance - input.param.y);
                #endif
                #if UNDERLAY_ON
                distance = tex2D(_MainTex, input.underlayUV.xy).a * input.underlayParam.x;
                color += float4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a) *
                    saturate(distance - input.underlayParam.y) * (1 - color.a);
                #endif
                #if UNDERLAY_INNER
                half sdf = saturate(distance - input.param.z);
                distance = tex2D(_MainTex, input.underlayUV.xy).a * input.underlayParam.x;
                color += float4(_UnderlayColor.rgb * _UnderlayColor.a, _UnderlayColor.a) *
                    (1 - saturate(distance - input.underlayParam.y)) * sdf * (1 - color.a);
                #endif
                #if UNITY_UI_CLIP_RECT
                half2 mask = saturate((_ClipRect.zw - _ClipRect.xy - abs(input.mask.xy)) *
                    input.mask.zw);
                color *= mask.x * mask.y;
                #endif
                #if (UNDERLAY_ON | UNDERLAY_INNER)
                color *= input.underlayUV.z;
                #endif
                #if UNITY_UI_ALPHACLIP
                clip(color.a - .001);
                #endif
                return color;
            }
            ENDCG
        }
    }
    CustomEditor "TMPro.EditorUtilities.TMP_SDFShaderGUI"
}
