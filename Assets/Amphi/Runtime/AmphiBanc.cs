// Banc de mesure : change un réglage de rendu SANS refabriquer l'APK.
//
// POURQUOI. Mesurer un levier, c'est comparer deux images du même endroit,
// au même instant, sur le casque. Refabriquer un APK par variante coûte une
// demi-heure et déplace forcément quelque chose entre les deux mesures. Ici,
// on pousse un fichier texte, l'application le relit en marche, et la mesure
// suivante porte sur la seule chose qui a changé.
//
//     adb push banc.txt /sdcard/Android/data/com.amphitraining.vr/files/banc.txt
//
// Le fichier est absent en usage normal : rien ne change alors, ce composant
// ne fait que regarder toutes les deux secondes si le fichier est apparu.
//
// Clés reconnues, une par ligne, « clé=valeur » :
//
//   foveation=0.25     niveau de fovéation fixe, 0 à 1
//   lodbias=1.4        QualitySettings.lodBias (le seuil effectif d'un
//                      niveau est seuil / lodBias)
//   lodmax=0           QualitySettings.maximumLODLevel
//   echelle=1.23       XRSettings.eyeTextureResolutionScale
//   msaa=4             échantillons MSAA de l'asset URP (1, 2, 4)
//   hdr=1              rendu HDR de l'asset URP
//   post=1             post-traitement de la caméra principale
//   bloom=1            effet Bloom du volume global (0 l'éteint seul)
//   bloomquart=0       1 : Bloom calculé au quart de la résolution
//   lppv=1             0 : lattes en sonde unique au lieu du volume de sondes
//   vue=orateur        impose le point de vue, quelle que soit l'orientation
//                      du casque posé : « orateur » (derrière le pupitre,
//                      face aux 228 places) ou « fond » (dernier rang, face
//                      à l'écran). Sans cette clé, deux mesures faites après
//                      qu'on a touché au casque ne regardent pas la même
//                      chose et ne se comparent pas.
//   masquer=Faisceaux,Micro,SIEGES
//                      équipements (AmphiEquipement.nom) à éteindre ;
//                      SIEGES éteint les 228 sièges, c'est la borne haute
//                      de tout ce qu'un levier sur les sièges peut rapporter
//
// Chaque application est écrite dans le journal sous le préfixe [AmphiBanc].

using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

namespace Amphi
{
    public class AmphiBanc : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Poser()
        {
            var go = new GameObject("AmphiBanc");
            DontDestroyOnLoad(go);
            go.AddComponent<AmphiBanc>();
        }

        string _chemin;
        System.DateTime _vu;
        readonly List<Renderer> _eteints = new List<Renderer>();

        IEnumerator Start()
        {
            _chemin = Path.Combine(Application.persistentDataPath, "banc.txt");
            // AmphiNettete pose sa fovéation au démarrage de XR : on passe
            // après elle, sinon c'est elle qui aurait le dernier mot.
            yield return new WaitForSeconds(5f);
            while (true)
            {
                Relire();
                yield return new WaitForSeconds(2f);
            }
        }

        void Relire()
        {
            if (!File.Exists(_chemin)) return;
            var t = File.GetLastWriteTimeUtc(_chemin);
            if (t == _vu) return;
            _vu = t;

            foreach (var r in _eteints) if (r != null) r.enabled = true;
            _eteints.Clear();

            var dit = new List<string>();
            foreach (var brut in File.ReadAllLines(_chemin))
            {
                var l = brut.Trim();
                int i = l.IndexOf('=');
                if (l.Length == 0 || l[0] == '#' || i < 0) continue;
                string k = l.Substring(0, i).Trim().ToLowerInvariant();
                string v = l.Substring(i + 1).Trim();
                float f;
                float.TryParse(v, System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out f);
                switch (k)
                {
                    case "foveation": dit.Add($"fovéation {Foveation(f):F2}"); break;
                    case "lodbias": QualitySettings.lodBias = f; dit.Add($"lodBias {f:F2}"); break;
                    case "lodmax": QualitySettings.maximumLODLevel = (int)f; dit.Add($"lodMax {(int)f}"); break;
                    case "echelle":
                        XRSettings.eyeTextureResolutionScale = f;
                        dit.Add($"échelle {XRSettings.eyeTextureResolutionScale:F2}");
                        break;
                    case "msaa":
                        if (Urp != null) Urp.msaaSampleCount = (int)f;
                        dit.Add($"MSAA {(Urp != null ? Urp.msaaSampleCount : -1)}");
                        break;
                    case "hdr":
                        if (Urp != null) Urp.supportsHDR = f > 0.5f;
                        dit.Add($"HDR {(Urp != null && Urp.supportsHDR)}");
                        break;
                    case "post":
                        var cam = Camera.main;
                        var donnees = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
                        if (donnees != null) donnees.renderPostProcessing = f > 0.5f;
                        dit.Add($"post {(donnees != null && donnees.renderPostProcessing)}");
                        break;
                    case "bloom":
                    case "bloomquart":
                        dit.Add(Bloom(k, f > 0.5f));
                        break;
                    case "lppv":
                        dit.Add($"lppv {(f > 0.5f ? "volume" : "sonde unique")} ({Lppv(f > 0.5f)} rendus)");
                        break;
                    case "vue":
                        dit.Add($"vue {v} : {Vue(v)}");
                        break;
                    case "masquer":
                        foreach (var nom in v.Split(','))
                            dit.Add($"masqué {nom.Trim()} ({Masquer(nom.Trim())} rendus)");
                        break;
                }
            }
            Debug.Log("[AmphiBanc] " + (dit.Count == 0 ? "aucun réglage" : string.Join(", ", dit)));
        }

        static string Bloom(string cle, bool oui)
        {
            foreach (var v in FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (v.sharedProfile == null || !v.sharedProfile.TryGet<Bloom>(out var b)) continue;
                if (cle == "bloom") b.active = oui;
                else b.downscale.Override(oui ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half);
                return $"bloom actif {b.active}, {b.downscale.value}";
            }
            return "bloom introuvable";
        }

        static int Lppv(bool volume)
        {
            int n = 0;
            foreach (var e in FindObjectsByType<AmphiEquipement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (e.genre != "claire-voie") continue;
                foreach (var r in e.GetComponentsInChildren<MeshRenderer>(true))
                {
                    r.lightProbeUsage = volume && r.lightProbeProxyVolumeOverride != null
                        ? LightProbeUsage.UseProxyVolume : LightProbeUsage.BlendProbes;
                    n++;
                }
            }
            return n;
        }

        static string Vue(string nom)
        {
            var rig = FindFirstObjectByType<AmphiRig>();
            if (rig == null || rig.head == null) return "rig introuvable";
            var sieges = FindObjectsByType<AmphiSiege>(FindObjectsSortMode.None);
            if (sieges.Length == 0) return "aucun siège";
            var centre = Vector3.zero;
            foreach (var s in sieges) centre += s.transform.position;
            centre /= sieges.Length;

            Vector3 oeil, cible;
            if (nom == "fond")
            {
                // le siège du dernier rang le plus proche de l'axe de la salle
                AmphiSiege loin = null;
                foreach (var s in sieges)
                    if (loin == null || s.rang > loin.rang
                        || (s.rang == loin.rang
                            && Mathf.Abs(s.transform.position.x - centre.x)
                               < Mathf.Abs(loin.transform.position.x - centre.x)))
                        loin = s;
                oeil = loin.transform.position + Vector3.up * 1.20f;
                var ecran = Equipement("Ecran_Principal");
                cible = ecran != null ? Bornes(ecran).center : centre;
            }
            else
            {
                var pupitre = Equipement("Pupitre");
                if (pupitre == null) return "pupitre introuvable";
                var b = Bornes(pupitre);
                var versSalle = centre - b.center; versSalle.y = 0f; versSalle.Normalize();
                // le pied du pupitre est posé sur l'estrade : c'est le sol de l'orateur
                oeil = new Vector3(b.center.x, b.min.y + 1.65f, b.center.z)
                       - versSalle * 0.45f;
                cible = centre;
            }

            // La pose de la tête est relative au rig : on place le rig pour
            // que la tête, là où le casque la met, tombe sur l'oeil voulu.
            var t = rig.transform;
            var tete = rig.head.transform;
            var voulu = Quaternion.LookRotation(cible - oeil, Vector3.up);
            var local = Quaternion.Inverse(t.rotation) * tete.rotation;
            t.rotation = voulu * Quaternion.Inverse(local);
            t.position += oeil - tete.position;
            return $"oeil {oeil.x:F2} {oeil.y:F2} {oeil.z:F2}";
        }

        static Transform Equipement(string nom)
        {
            foreach (var e in FindObjectsByType<AmphiEquipement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (e.nom == nom) return e.transform;
            return null;
        }

        static Bounds Bornes(Transform t)
        {
            var rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(t.position, Vector3.zero);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static UniversalRenderPipelineAsset Urp =>
            GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        static float Foveation(float niveau)
        {
            var liste = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(liste);
            foreach (var d in liste)
            {
                if (d == null || !d.running) continue;
                try { d.foveatedRenderingLevel = Mathf.Clamp01(niveau); return d.foveatedRenderingLevel; }
                catch { }
            }
            return -1f;
        }

        int Masquer(string nom)
        {
            int n = 0;
            if (nom == "SIEGES")
            {
                foreach (var s in FindObjectsByType<AmphiSiege>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    foreach (var r in s.GetComponentsInChildren<Renderer>(true))
                        if (r.enabled) { r.enabled = false; _eteints.Add(r); n++; }
                return n;
            }
            foreach (var e in FindObjectsByType<AmphiEquipement>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (e.nom != nom) continue;
                foreach (var r in e.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; _eteints.Add(r); n++; }
            }
            return n;
        }
    }
}
