// Le halo affectif d'un agent : une lueur douce derrière lui, qui donne à
// voir son HUMEUR (AmphiAffect) par sa température de couleur, sa clarté,
// sa respiration et son ampleur.
//
// Un quad tourné vers l'orateur, une décroissance radiale calculée ici
// (pas de texture), en mélange ADDITIF : posé un peu derrière le robot, il
// déborde de sa silhouette comme une aura, et s'efface sans bord net. La
// couleur, l'intensité et la forme arrivent par MaterialPropertyBlock : une
// seule matière pour les dix halos.
//
// _Forme.x : rayon du coeur lumineux (0..1), _Forme.y : douceur du bord,
// _Forme.z : intensité d'un anneau (un pic d'émotion qui s'élargit),
// _Forme.w : rayon de cet anneau.
//
// Écrit à la main, comme AmphiPanneauVerre : un shader référencé par une
// matière de la scène ne manque jamais au build. Stéréo instanciée du Quest
// prise en charge.

Shader "Amphi/Halo"
{
    Properties
    {
        _Couleur ("Couleur", Color) = (1, 0.8, 0.5, 1)
        _Intensite ("Intensité", Range(0, 4)) = 1
        _Forme ("Coeur, douceur, anneau, rayon de l'anneau", Vector) = (0.25, 0.75, 0, 0.6)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Halo"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Couleur;
                half _Intensite;
                half4 _Forme;
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
                o.uv = i.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float r = length(i.uv * 2.0 - 1.0);
                // coeur doux puis décroissance en cloche jusqu'au bord du quad
                float coeur = 1.0 - smoothstep(_Forme.x, _Forme.x + _Forme.y, r);
                float halo = coeur * coeur;
                // l'anneau d'un pic d'émotion
                float anneau = _Forme.z * exp(-pow((r - _Forme.w) / 0.06, 2.0));
                float a = saturate(halo + anneau) * (1.0 - smoothstep(0.92, 1.0, r));
                return half4(_Couleur.rgb * a * _Intensite, 0);
            }
            ENDHLSL
        }
    }
}
