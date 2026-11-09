// Netteté de l'image en VR : réglage et MESURE.
//
// LE PROBLEME QUE CE FICHIER TRAITE.
//
// Un Quest 3 affiche 2064 x 2208 pixels par oeil, mais Unity lui rend par
// défaut 1680 x 1760. On calcule donc une image plus petite que l'écran, et
// le compositeur l'étire. Cette perte-là est en AVAL de tout le reste :
// aucune texture, aucun anticrénelage, aucun éclairage ne la rattrape, parce
// qu'elle se produit après eux.
//
// 2064 / 1680 = 1,229 : c'est le facteur qui rend un pixel calculé pour un
// pixel de dalle. Il se règle sur l'asset URP, à la construction.
//
// CE FICHIER NE SE CONTENTE PAS DE REGLER : IL RELIT.
//
// Un facteur posé n'est pas un facteur appliqué. Le pilote peut le borner,
// le casque peut imposer sa propre définition, une version d'OpenXR peut
// ignorer la propriété. On écrit donc dans le journal la définition
// REELLEMENT allouée, sous un préfixe filtrable :
//
//     adb logcat -d | findstr AmphiNet
//
// C'est la seule façon de savoir, depuis une machine de développement, ce que
// le casque a vraiment fait — d'autant qu'une application Quest ne démarre
// pas tant qu'elle n'est pas portée, manettes allumées.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Amphi
{
    public class AmphiNettete : MonoBehaviour
    {
        /// <summary>
        /// Force du rendu fovéal, de 0 (aucun) à 1 (maximum).
        /// </summary>
        /// <remarks>
        /// C'est lui qui PAIE la montée en définition : il abaisse la finesse
        /// en périphérie du champ, là où l'oeil ne distingue pas le détail, et
        /// rend le budget au centre. Passer de 1680 à 2064 px par oeil coûte
        /// 51 % de pixels ; la fovéation en reprend une bonne part sans que
        /// cela se voie, parce qu'elle ne dégrade que ce qu'on ne regarde pas.
        ///
        /// 0,5 est un compromis prudent. Au-delà de 0,7 la baisse devient
        /// perceptible en bord de champ quand on bouge la tête vite.
        /// </remarks>
        [Range(0f, 1f)] public float foveation = 0.5f;

        /// <summary>Intervalle entre deux relevés, en secondes. 0 = un seul.</summary>
        public float periodeReleve = 0f;

        void Start() { StartCoroutine(Regler()); }

        IEnumerator Regler()
        {
            // On attend que le sous-système d'affichage existe : au premier
            // Start, XR n'est pas toujours démarré, et tout relevé fait à ce
            // moment-là ne décrirait rien.
            XRDisplaySubsystem ecran = null;
            var liste = new List<XRDisplaySubsystem>();
            for (int i = 0; i < 300 && ecran == null; i++)
            {
                liste.Clear();
                SubsystemManager.GetSubsystems(liste);
                foreach (var d in liste)
                    if (d != null && d.running) { ecran = d; break; }
                if (ecran == null) yield return null;
            }

            if (ecran == null)
            {
                Debug.Log("[AmphiNet] aucun affichage XR actif : lancé hors casque, "
                          + "les réglages de netteté ne s'appliquent pas.");
                yield break;
            }

            // La fovéation, posée PUIS relue. La propriété peut être ignorée
            // si l'API de fovéation du projet n'est pas « SRP Foveation ».
            float demande = Mathf.Clamp01(foveation);
            try { ecran.foveatedRenderingLevel = demande; }
            catch (System.Exception e)
            {
                Debug.Log("[AmphiNet] fovéation refusée : " + e.Message);
            }

            do
            {
                float obtenu = 0f;
                try { obtenu = ecran.foveatedRenderingLevel; } catch { }

                // La définition par oeil, telle que le casque l'a réellement
                // allouée. C'est LE chiffre qui dit si la netteté est là.
                int l = XRSettings.eyeTextureWidth;
                int h = XRSettings.eyeTextureHeight;
                float rapport = l > 0 ? l / 1680f : 0f;

                Debug.Log($"[AmphiNet] oeil {l} x {h} px"
                          + $"  (dalle Quest 3 : 2064 x 2208, soit {100f * l / 2064f:F0} %)"
                          + $"  facteur effectif {rapport:F2}"
                          + $"  fovéation demandée {demande:F2} obtenue {obtenu:F2}"
                          + $"  MSAA {QualitySettings.antiAliasing}x"
                          + $"  aniso {QualitySettings.anisotropicFiltering}");

                if (l > 0 && l < 2000)
                    Debug.Log("[AmphiNet] ATTENTION : la définition rendue reste sous "
                              + "celle de la dalle. Le facteur de rendu de l'asset URP "
                              + "n'a pas été appliqué, ou le casque l'a borné.");

                if (periodeReleve <= 0f) yield break;
                yield return new WaitForSeconds(periodeReleve);
            } while (true);
        }
    }
}
