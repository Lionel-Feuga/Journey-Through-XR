// Coeur de la lame : blanc au centre, couleur du sabre sur les bords. Non éclairé.
// _CoreIntensity : quantité de blanc au centre. _CorePower : finesse de ce centre blanc.
// _Intensity : force de la couleur, surtout visible sur les bords (au-delà de 1, elle alimente le Bloom).
Shader "JourneyXR/SaberBlade"
{
    Properties
    {
        [HDR] _Color ("Couleur", Color) = (0.1, 0.45, 1, 1)
        _CoreIntensity ("Blanc du centre", Range(0, 4)) = 3.35
        _CorePower ("Finesse du centre blanc", Range(0.2, 8)) = 1.15
        _Intensity ("Couleur des bords (au-delà de 1 : alimente le Bloom)", Range(0, 8)) = 5.95
        [HideInInspector] _Flicker ("Scintillement (piloté par script)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _CorePower;
            half _CoreIntensity;
            half _Intensity;
            half _Flicker;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS : TEXCOORD0;
            float3 viewDirWS : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        Varyings Vert(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.viewDirWS = GetWorldSpaceViewDir(positionWS);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                half core = pow(facing, _CorePower);
                // Couleur en HDR + coeur blanc : à l'écran le centre reste blanc, mais le Bloom diffuse la couleur.
                half3 color = (_Color.rgb * _Intensity + core * _CoreIntensity) * _Flicker;
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma multi_compile_instancing

            half4 FragDepth(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
