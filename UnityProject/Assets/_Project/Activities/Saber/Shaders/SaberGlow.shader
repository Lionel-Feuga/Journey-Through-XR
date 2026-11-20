// Halo de la lame : coque additive, intense de face et qui s'éteint sur les bords.
// _Opacity à 0 : purement additif (se perd sur fond clair). Vers 1 : le halo recouvre le décor et reste visible partout.
// Donne l'effet de brillance sans post-traitement (Bloom), donc utilisable sur Quest.
Shader "JourneyXR/SaberGlow"
{
    Properties
    {
        [HDR] _Color ("Couleur", Color) = (0.1, 0.45, 1, 1)
        _Intensity ("Intensité", Range(0, 8)) = 2
        _Falloff ("Resserrement du halo", Range(0.5, 10)) = 10
        _Opacity ("Opacité sur fond clair", Range(0, 1)) = 0.8
        [HideInInspector] _Flicker ("Scintillement (piloté par script)", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Falloff;
                half _Opacity;
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half facing = saturate(dot(normalize(input.normalWS), normalize(input.viewDirWS)));
                half shape = pow(facing, _Falloff);
                half glow = shape * _Intensity * _Flicker;
                return half4(_Color.rgb * glow, saturate(shape * _Opacity));
            }
            ENDHLSL
        }
    }
}
