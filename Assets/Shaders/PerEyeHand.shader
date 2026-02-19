Shader "Custom/PerEyeHand"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.02, 0.08, 0.2, 0.15)
        _TargetEye ("Target Eye (0=Left, 1=Right)", Float) = 0
        
        [Header(Edge Glow)]
        _EdgeColor ("Edge Color", Color) = (0, 1, 1, 1)
        _EdgePower ("Edge Power", Range(0.5, 5)) = 1.5
        _EdgeIntensity ("Edge Intensity", Range(1, 10)) = 4.0
        _EdgeWidth ("Edge Width", Range(0.1, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PerEyeForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ UNITY_SINGLE_PASS_STEREO STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewDirWS  : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                float  _TargetEye;
                half4  _EdgeColor;
                float  _EdgePower;
                float  _EdgeIntensity;
                float  _EdgeWidth;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = vpi.positionCS;
                o.positionWS = vpi.positionWS;
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS  = normalize(GetWorldSpaceViewDir(vpi.positionWS));
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 每眼裁剪
                if (abs((float)unity_StereoEyeIndex - _TargetEye) > 0.5)
                    discard;

                float3 N = normalize(input.normalWS);
                float3 V = normalize(input.viewDirWS);
                float NdotV = saturate(dot(N, V));
                
                // ═══════════════════════════════════════════════════════════
                // 边缘检测：NdotV 接近 0 = 边缘，接近 1 = 正面
                // ═══════════════════════════════════════════════════════════
                
                // 边缘强度：边缘处最亮
                float edge = 1.0 - NdotV;
                float edgeFactor = pow(edge, _EdgePower);
                
                // 硬边缘：让边缘更锐利
                float hardEdge = smoothstep(1.0 - _EdgeWidth, 1.0, edge);
                
                // 组合边缘效果
                float finalEdge = max(edgeFactor, hardEdge * 0.8);
                
                // ═══════════════════════════════════════════════════════════
                // 颜色混合
                // ═══════════════════════════════════════════════════════════
                
                // 基础颜色（内部暗色）
                half4 col = _BaseColor;
                
                // 边缘发光
                half3 glowColor = _EdgeColor.rgb * _EdgeIntensity;
                col.rgb = lerp(col.rgb, glowColor, saturate(finalEdge));
                
                // Alpha：边缘更不透明
                col.a = lerp(_BaseColor.a, 0.9, saturate(finalEdge * 1.5));
                
                return col;
            }
            ENDHLSL
        }

        // Shadow caster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings  { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                return o;
            }
            half4 frag(Varyings i) : SV_Target { discard; return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Unlit"
}
