// Fondu au noir très court pendant un saut de téléportation.
//
// Ce n'est pas une coquetterie : un changement de point de vue instantané est
// la première cause de malaise en VR. Une centaine de millisecondes de noir
// suffit à couper le flux visuel et à supprimer la sensation de saut.
//
// Le fondu est un quad tenu devant l'œil, pas un effet d'écran : il ne
// dépend d'aucun pipeline de rendu, donc il fonctionne en URP comme en
// pipeline intégré, et il ne coûte rien.

using System.Collections;
using UnityEngine;

namespace Amphi
{
    public class AmphiFondu : MonoBehaviour
    {
        public Renderer voile;
        public Color couleur = Color.black;

        MaterialPropertyBlock _bloc;
        Coroutine _encours;
        static readonly int IdBase = Shader.PropertyToID("_BaseColor");
        static readonly int IdCouleur = Shader.PropertyToID("_Color");

        void Awake()
        {
            _bloc = new MaterialPropertyBlock();
            Alpha(0f);
        }

        public void Clignement(float duree)
        {
            if (voile == null) return;
            if (_encours != null) StopCoroutine(_encours);
            _encours = StartCoroutine(Cycle(Mathf.Max(0.02f, duree)));
        }

        IEnumerator Cycle(float duree)
        {
            for (float t = 0f; t < duree; t += Time.unscaledDeltaTime)
            {
                Alpha(Mathf.Clamp01(t / duree));
                yield return null;
            }
            Alpha(1f);
            yield return null;                  // le saut a lieu ici, écran noir
            for (float t = 0f; t < duree; t += Time.unscaledDeltaTime)
            {
                Alpha(1f - Mathf.Clamp01(t / duree));
                yield return null;
            }
            Alpha(0f);
            _encours = null;
        }

        void Alpha(float a)
        {
            if (voile == null) return;
            voile.enabled = a > 0.001f;
            var c = couleur;
            c.a = a;
            _bloc.SetColor(IdBase, c);
            _bloc.SetColor(IdCouleur, c);
            voile.SetPropertyBlock(_bloc);
        }
    }
}
