// Test direct d'une famille de lampes, SANS CUISSON.
//
// POURQUOI. Deux corrections successives des lèche-murs — orientation, puis
// hauteur et distance — n'ont pas bougé la luminance des parois d'un millième.
// Chaque essai coûtait douze minutes de cuisson pour un résultat identique à
// trois décimales. Itérer ainsi, c'est tâtonner.
//
// Une lampe CUITE ne se teste qu'après la cuisson. Une lampe TEMPS REEL
// s'évalue immédiatement. On bascule donc la famille en temps réel, on éteint
// tout le reste, et on rend : si la paroi reste noire, la lumière n'arrive pas
// et le problème est géométrique ; si elle s'éclaire, le problème est dans la
// puissance ou dans la cuisson. Le test tranche entre les deux en une minute.
//
// Rien n'est enregistré : la scène est rechargée à la fin.

using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AmphiEditor
{
    public static class AmphiTestLampe
    {
        const string Scene = "Assets/Amphi/Scenes/Amphi.unity";

        [MenuItem("AmphiTraining/9 quater. Tester les lèche-murs (sans cuisson)")]
        public static void TesterLecheMurs() { Tester("LECHE"); }

        static void Tester(string prefixe)
        {
            EditorSceneManager.OpenScene(Scene);

            var toutes = UnityEngine.Object.FindObjectsByType<Light>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            var cibles = toutes.Where(l => l.name.StartsWith(prefixe)).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"[Amphi] TEST DIRECT « {prefixe} » (sans cuisson)");
            sb.AppendLine($"  {cibles.Count} lampes portent ce préfixe, "
                          + $"sur {toutes.Length} au total");
            if (cibles.Count == 0)
            {
                sb.AppendLine("  ECHEC  aucune lampe de ce nom dans la scène : "
                              + "elles ne sont pas arrivées jusqu'ici.");
                Debug.Log(sb.ToString());
                if (Application.isBatchMode) EditorApplication.Exit(0);
                return;
            }

            foreach (var l in cibles)
                sb.AppendLine($"    {l.name}  type {l.type}  intensité {l.intensity:F2}"
                              + $"  portée {l.range:F1}  angle {l.spotAngle:F0}°"
                              + $"  pos ({l.transform.position.x:F1}, "
                              + $"{l.transform.position.y:F1}, {l.transform.position.z:F1})"
                              + $"  vise {l.transform.forward}");

            // Tout en temps réel pour les cibles, tout éteint pour les autres.
            foreach (var l in toutes)
            {
                if (cibles.Contains(l))
                {
                    l.lightmapBakeType = LightmapBakeType.Realtime;
                    l.enabled = true;
                    l.shadows = LightShadows.None;   // on teste l'arrivée, pas l'ombre
                }
                else l.enabled = false;
            }
            // Et pas d'ambiante, sinon on mesure elle et non les lampes.
            var ambAvant = RenderSettings.ambientMode;
            var intAvant = RenderSettings.ambientIntensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;

            // On vise la paroi depuis le milieu de la salle.
            var prises = new (string nom, Vector3 oeil, Vector3 cible)[]
            {
                ("paroi_gauche", new Vector3(0f, 3.0f, 2.0f), new Vector3(-13f, 3.0f, 2.0f)),
                ("paroi_droite", new Vector3(0f, 3.0f, 2.0f), new Vector3(13f, 3.0f, 2.0f)),
                ("fond",         new Vector3(0f, 4.0f, 2.0f), new Vector3(0f, 4.0f, -14f)),
            };

            var go = new GameObject("CAM_TEST") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var dossier = Path.Combine(
                Path.GetDirectoryName(Application.dataPath), "Vues");
            Directory.CreateDirectory(dossier);

            foreach (var p in prises)
            {
                go.transform.position = p.oeil;
                go.transform.rotation = Quaternion.LookRotation(p.cible - p.oeil);
                var rt = new RenderTexture(900, 700, 24, RenderTextureFormat.ARGB32,
                                           RenderTextureReadWrite.sRGB);
                cam.targetTexture = rt;
                cam.Render();
                var avant = RenderTexture.active;
                RenderTexture.active = rt;
                var img = new Texture2D(900, 700, TextureFormat.RGB24, false);
                img.ReadPixels(new Rect(0, 0, 900, 700), 0, 0);
                img.Apply();
                RenderTexture.active = avant;
                cam.targetTexture = null;

                double s = 0; float max = 0f;
                foreach (var c in img.GetPixels())
                {
                    float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    s += y; if (y > max) max = y;
                }
                float moy = (float)(s / (900.0 * 700.0));
                var chemin = Path.Combine(dossier, "test_" + p.nom + ".png");
                File.WriteAllBytes(chemin, img.EncodeToPNG());
                sb.AppendLine($"  {p.nom,-14} luminance moyenne {moy:F4}, max {max:F3}"
                              + $"  -> {chemin}");
                UnityEngine.Object.DestroyImmediate(img);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }

            sb.AppendLine();
            sb.AppendLine("  Lecture : un max proche de 0 veut dire que la lumière");
            sb.AppendLine("  n'ARRIVE PAS (géométrie, orientation, occlusion). Un max");
            sb.AppendLine("  franc sur une moyenne faible veut dire qu'elle arrive mais");
            sb.AppendLine("  qu'elle est trop concentrée ou trop faible.");

            UnityEngine.Object.DestroyImmediate(go);
            RenderSettings.ambientMode = ambAvant;
            RenderSettings.ambientIntensity = intAvant;

            Debug.Log(sb.ToString());
            // La scène a été salie : on la recharge sans rien enregistrer.
            EditorSceneManager.OpenScene(Scene);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
