Shader "Custom/PerEyeHand"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.6, 0.85, 1.0, 0.35)
        _TargetEye ("Target Eye (0=Left, 1=Right)", Float) = 0
        _FresnelPower ("Fresnel Power", Float) = 2.5
        _FresnelColor ("Fresnel Color", Color) = (0, 1, 1, 0.8)
        _RimIntensity ("Rim Intensity", Float) = 1.5
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
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                float  _TargetEye;
                float  _FresnelPower;
                half4  _FresnelColor;
                float  _RimIntensity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs vpi = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = vpi.positionCS;
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                o.viewDirWS  = normalize(GetWorldSpaceViewDir(vpi.positionWS));
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ── 每眼裁剪：不属于目标眼则丢弃 ──
                if (abs((float)unity_StereoEyeIndex - _TargetEye) > 0.5)
                    discard;

                // ── 菲涅尔边缘发光 ──
                float3 N = normalize(input.normalWS);
                float3 V = normalize(input.viewDirWS);
                float  NdotV = saturate(dot(N, V));
                float  fresnel = pow(1.0 - NdotV, _FresnelPower) * _RimIntensity;

                half4 col = _BaseColor;
                col.rgb = lerp(col.rgb, _FresnelColor.rgb, saturate(fresnel) * _FresnelColor.a);
                col.a   = lerp(col.a,   1.0h, saturate(fresnel) * 0.6h);

                return col;
            }
            ENDHLSL
        }

        // ── Shadow caster pass（防止透明物体投射实心阴影）──
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

