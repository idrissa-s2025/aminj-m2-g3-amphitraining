// Le verre du panneau du présentateur : une image déjà composée, posée telle
// quelle, en alpha PREMULTIPLIE.
//
// POURQUOI PREMULTIPLIE. L'interface est rendue dans une texture vidée à
// (0, 0, 0, 0) : chaque pixel y porte sa couleur déjà multipliée par sa
// couverture. Le fond de verre donne rgb = fond x a ; un texte
// opaque par-dessus donne sa couleur pleine et une couverture de 1 ; hors
// des coins arrondis, tout est à zéro. Le mélange juste pour une telle image
// est One / OneMinusSrcAlpha. Le mode « Alpha » d'URP multiplierait une
// seconde fois, et son mode « Premultiply » aussi, dans le shader : les bords
// de lettre se seraient assombris d'un liseré.
//
// Écrit à la main plutôt que tiré de l'Unlit d'URP : un variant transparent
// fabriqué au vol peut manquer au build, un shader référencé par une matière
// de la scène ne manque jamais.

Shader "Amphi/PanneauVerre"
{
    Properties
    {
        _BaseMap ("Image", 2D) = "black" {}
        _Opacite ("Opacité", Range(0, 1)) = 1
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
            Name "Verre"
            Tags { "LightMode" = "UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            // vu de dos, le panneau reste visible, comme ceux du Quest
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Opacite;
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

            Varyings Vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _Opacite;
            }
            ENDHLSL
        }
    }
}
