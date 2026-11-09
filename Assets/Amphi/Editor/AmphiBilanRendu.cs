// Bilan du rendu : ce qu'on VOIT, mesuré, et non ce qu'on croit avoir posé.
//
// POURQUOI CE FICHIER EXISTE.
//
// « La salle est sombre » a été corrigé trois fois, et trois fois elle est
// restée sombre. A chaque passe on mesurait une grandeur juste mais qui ne
// répondait pas à la question : l'albédo des matières, la luminance des
// atlas, le nombre de lampes. Aucune de ces trois-là ne dit ce qui remplit le
// champ de vision de quelqu'un qui tourne la tête.
//
// Or c'est cela, et cela seul, qui fait qu'une salle paraît sombre. Une
// matière à 0,70 d'albédo qui couvre 2 % de l'image ne sauve rien ; une
// matière à 0,05 qui en couvre 40 % décide de tout. Tant qu'on ne croise pas
// la PART DU CHAMP DE VISION avec la LUMINANCE REELLE A L'IMAGE, on corrige
// au hasard.
//
// Ce fichier fait donc, pour chaque point de vue, deux rendus :
//
//   1. le rendu normal, dont on lit la luminance pixel par pixel ;
//   2. un rendu d'IDENTIFICATION, où chaque matière est remplacée par une
//      couleur plate et unique, sans éclairage ni anticrénelage.
//
// En les croisant, on obtient un tableau : pour chaque matière, le nombre de
// pixels qu'elle occupe et la luminance moyenne qu'elle présente. La colonne
// qui compte est « part x obscurité » : c'est l'ordre dans lequel corriger.
//
// Il ne modifie RIEN de durable : les matériaux sont remis en place dans un
// `finally`, et la scène n'est jamais enregistrée.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AmphiEditor
{
    public static class AmphiBilanRendu
    {
        const string Scene = "Assets/Amphi/Scenes/Amphi.unity";
        const int Largeur = 1024;
        const int Hauteur = 1024;

        /// <summary>Un point de vue : d'où l'on regarde, et vers où.</summary>
        struct Vue
        {
            public string nom;
            public Vector3 oeil;
            public float lacet;      // degrés, 0 = vers la scène
            public float tangage;    // degrés, positif = vers le haut
        }

        [MenuItem("AmphiTraining/9 ter. Bilan du rendu (ce qu'on voit)")]
        public static void Bilan()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager
                    .GetActiveScene().path.EndsWith("Amphi.unity"))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);

            // Trois positions, parce qu'une salle ne se juge pas d'un point :
            // le premier rang, le milieu, le dernier rang. A chacune, quatre
            // directions plus un regard vers le haut — en VR on lève les yeux,
            // et c'est précisément là qu'est le plafond.
            var positions = new (string nom, Vector3 p)[]
            {
                ("rang_avant", new Vector3(0f, 1.35f, -2.5f)),
                ("milieu",     new Vector3(0f, 2.60f, 3.0f)),
                ("rang_fond",  new Vector3(0f, 4.30f, 9.0f)),
            };
            var vues = new List<Vue>();
            foreach (var (nom, p) in positions)
            {
                foreach (var lacet in new[] { 0f, 90f, 180f, 270f })
                    vues.Add(new Vue { nom = $"{nom}_{lacet:F0}", oeil = p,
                                       lacet = lacet, tangage = 0f });
                vues.Add(new Vue { nom = $"{nom}_haut", oeil = p,
                                   lacet = 0f, tangage = 55f });
            }

            // Les rendus concernés : tout ce qui est visible depuis la salle.
            var rendus = new List<MeshRenderer>();
            foreach (var racine in new[] { "AMPHI", "RIG_VR" })
            {
                var g = GameObject.Find(racine);
                if (g != null) rendus.AddRange(g.GetComponentsInChildren<MeshRenderer>(true));
            }
            rendus = rendus.Where(r => r != null && r.enabled
                                       && r.gameObject.activeInHierarchy).ToList();

            // Une couleur par matière. Le canal rouge porte l'index, par pas de
            // 4 pour survivre à l'encodage ; 63 matières au plus, on en a 25.
            var noms = new List<string>();
            var index = new Dictionary<string, int>();
            foreach (var r in rendus)
                foreach (var m in r.sharedMaterials)
                {
                    string n = m == null ? "(sans matériau)"
                                         : m.name.Replace(" (Instance)", "");
                    if (!index.ContainsKey(n)) { index[n] = noms.Count; noms.Add(n); }
                }
            if (noms.Count > 63)
            {
                Debug.LogError($"[Amphi] {noms.Count} matières : le codage "
                               + "d'identification n'en tient que 63.");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }

            var shaderPlat = Shader.Find("Universal Render Pipeline/Unlit")
                             ?? Shader.Find("Unlit/Color");
            if (shaderPlat == null)
            {
                Debug.LogError("[Amphi] aucun shader non éclairé : bilan impossible.");
                if (Application.isBatchMode) EditorApplication.Exit(2);
                return;
            }
            var matsId = new Material[noms.Count];
            for (int i = 0; i < noms.Count; i++)
            {
                var c = new Color(i * 4 / 255f, 0f, 0f, 1f);
                matsId[i] = new Material(shaderPlat) { hideFlags = HideFlags.HideAndDontSave };
                if (matsId[i].HasProperty("_BaseColor")) matsId[i].SetColor("_BaseColor", c);
                if (matsId[i].HasProperty("_Color")) matsId[i].SetColor("_Color", c);
            }

            var go = new GameObject("CAM_BILAN") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 90f;          // proche du champ utile d'un casque
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;

            // Compteurs cumulés sur toutes les vues.
            var pixels = new long[noms.Count];
            var somme = new double[noms.Count];
            long total = 0, sombres05 = 0, sombres10 = 0, sombres20 = 0;
            var sb = new StringBuilder();
            var dossier = Path.Combine(
                Path.GetDirectoryName(Application.dataPath), "Vues");
            Directory.CreateDirectory(dossier);

            // Sauvegarde des matériaux, pour les remettre quoi qu'il arrive.
            var sauvegarde = rendus.ToDictionary(r => r, r => r.sharedMaterials);
            try
            {
                foreach (var v in vues)
                {
                    go.transform.position = v.oeil;
                    go.transform.rotation = Quaternion.Euler(-v.tangage, v.lacet, 0f);

                    var beaute = Rendre(cam, true);
                    var ids = RendreIds(cam, rendus, sauvegarde, matsId, index);

                    for (int i = 0; i < beaute.Length; i++)
                    {
                        var c = beaute[i];
                        float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                        total++;
                        if (y < 0.05f) sombres05++;
                        if (y < 0.10f) sombres10++;
                        if (y < 0.20f) sombres20++;
                        int k = Mathf.RoundToInt(ids[i].r * 255f) / 4;
                        if (k >= 0 && k < noms.Count) { pixels[k]++; somme[k] += y; }
                    }
                }
            }
            finally
            {
                foreach (var kv in sauvegarde)
                    if (kv.Key != null) kv.Key.sharedMaterials = kv.Value;
                UnityEngine.Object.DestroyImmediate(go);
                foreach (var m in matsId) UnityEngine.Object.DestroyImmediate(m);
            }

            // ------------------------------------------------------- rapport
            var ci = CultureInfo.InvariantCulture;
            sb.AppendLine("[Amphi] BILAN DU RENDU");
            sb.AppendLine($"  {vues.Count} vues, {Largeur}x{Hauteur}, champ 90°, "
                          + $"{total / 1000000.0:F1} Mpx analysés");
            sb.AppendLine($"  part du champ sous 0,05 de luminance : "
                          + $"{100.0 * sombres05 / total:F1} %");
            sb.AppendLine($"  part du champ sous 0,10               : "
                          + $"{100.0 * sombres10 / total:F1} %");
            sb.AppendLine($"  part du champ sous 0,20               : "
                          + $"{100.0 * sombres20 / total:F1} %");
            sb.AppendLine();
            sb.AppendLine("  matière                    part     luminance   manque");
            sb.AppendLine("  (le manque = part x (0,25 - luminance), borné à 0 :");
            sb.AppendLine("   c'est l'ordre dans lequel corriger)");

            var lignes = new List<(string nom, double part, double lum, double manque)>();
            for (int i = 0; i < noms.Count; i++)
            {
                if (pixels[i] == 0) continue;
                double part = (double)pixels[i] / total;
                double lum = somme[i] / pixels[i];
                lignes.Add((noms[i], part, lum, part * Math.Max(0.0, 0.25 - lum)));
            }
            foreach (var l in lignes.OrderByDescending(x => x.manque))
                sb.AppendLine(string.Format(ci,
                    "  {0,-24} {1,6:F2} %   {2,7:F3}   {3,7:F4}",
                    l.nom, 100.0 * l.part, l.lum, l.manque));

            sb.AppendLine();
            sb.AppendLine("  Les trois premières lignes disent où est le noir.");
            Debug.Log(sb.ToString());
            File.WriteAllText(Path.Combine(dossier, "bilan_rendu.txt"), sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>Rend la vue courante et renvoie ses pixels.</summary>
        static Color[] Rendre(Camera cam, bool avecEffets)
        {
            var rt = new RenderTexture(Largeur, Hauteur, 24,
                                       RenderTextureFormat.ARGB32,
                                       RenderTextureReadWrite.sRGB);
            // L'anticrénelage MELANGE les pixels : il embellit le rendu normal
            // et il rendrait les identifiants illisibles, puisqu'une moyenne
            // de deux identifiants en désigne un troisième.
            rt.antiAliasing = avecEffets ? 4 : 1;
            cam.targetTexture = rt;
            cam.clearFlags = avecEffets ? CameraClearFlags.Skybox
                                        : CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.Render();
            var avant = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(Largeur, Hauteur, TextureFormat.RGBA32, false);
            img.ReadPixels(new Rect(0, 0, Largeur, Hauteur), 0, 0);
            img.Apply();
            RenderTexture.active = avant;
            cam.targetTexture = null;
            var px = img.GetPixels();
            UnityEngine.Object.DestroyImmediate(img);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
            return px;
        }

        /// <summary>
        /// Rend la passe d'identification : chaque matière en aplat unique.
        /// </summary>
        static Color[] RendreIds(Camera cam, List<MeshRenderer> rendus,
                                 Dictionary<MeshRenderer, Material[]> sauvegarde,
                                 Material[] matsId, Dictionary<string, int> index)
        {
            foreach (var r in rendus)
            {
                var orig = sauvegarde[r];
                var remp = new Material[orig.Length];
                for (int s = 0; s < orig.Length; s++)
                {
                    string n = orig[s] == null ? "(sans matériau)"
                                               : orig[s].name.Replace(" (Instance)", "");
                    remp[s] = matsId[index[n]];
                }
                r.sharedMaterials = remp;
            }
            var px = Rendre(cam, false);
            foreach (var r in rendus) r.sharedMaterials = sauvegarde[r];
            return px;
        }
    }
}
