// Diagnostic de l'éclairage cuit, mesuré plutôt que jugé à l'oeil.
//
// Une salle qui sort noire et striée dans le casque pose une question à
// laquelle une capture d'écran ne répond pas : la cuisson est-elle absente,
// mal appliquée, ou correctement appliquée sur un dépliage abîmé ? Les trois
// se ressemblent à l'image et se corrigent différemment.
//
// Ce fichier ne modifie RIEN. Il ouvre la scène, lit ce que le moteur a
// réellement en mémoire, et l'écrit.
//
//   Unity -batchmode -quit -projectPath <projet> \
//         -executeMethod AmphiEditor.AmphiDiagEclairage.Rapport \
//         -logFile diag.log
//
// Ce qu'il faut regarder, dans l'ordre :
//
//   lightmapIndex     -1 = l'objet n'est PAS cuit. Statique oublié, ou exclu
//                     de la contribution GI.
//   échelle UV        la part d'atlas donnée à l'objet. C'est elle qui décide
//                     de la finesse : une grande surface à faible échelle
//                     reçoit quelques texels étirés, et c'est exactement ce
//                     qui fabrique des traînées.
//   texels par mètre  la même chose, ramenée au réel et comparable à la
//                     consigne de cuisson (12 px/unité ici).
//   UV2 dégénérés     triangles d'aire nulle dans le dépliage de lightmap :
//                     ils s'étirent sur tout ce qui les entoure.
//   UV2 hors [0,1]    un dépliage qui déborde de son carreau échantillonne
//                     l'atlas du voisin.

using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace AmphiEditor
{
    public static class AmphiDiagEclairage
    {
        const string Scene = "Assets/Amphi/Scenes/Amphi.unity";

        [MenuItem("AmphiTraining/9. Diagnostiquer l'éclairage cuit")]
        public static void Rapport()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager
                    .GetActiveScene().path.EndsWith("Amphi.unity"))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);

            var sb = new StringBuilder();
            sb.AppendLine("[Amphi] DIAGNOSTIC DE L'ECLAIRAGE CUIT");

            // 1. Les atlas effectivement chargés par la scène.
            var maps = LightmapSettings.lightmaps;
            sb.AppendLine($"  atlas chargés    {maps.Length}");
            for (int i = 0; i < maps.Length; i++)
            {
                var c = maps[i].lightmapColor;
                sb.AppendLine($"    [{i}] {(c == null ? "AUCUNE TEXTURE" : c.width + " x " + c.height)}");
            }
            sb.AppendLine($"  mode             {LightmapSettings.lightmapsMode}");
            if (maps.Length == 0)
                sb.AppendLine("  ECHEC  aucun atlas dans la scène : la cuisson n'a pas été "
                              + "sauvegardée, ou le LightingDataAsset n'est pas lié.");

            // 2. Chaque rendu du décor et des équipements.
            sb.AppendLine("  ---- rendus ----");
            foreach (var racine in new[] { "AMPHI/DECOR", "AMPHI/EQUIPEMENTS" })
            {
                var go = GameObject.Find(racine);
                if (go == null) { sb.AppendLine($"  {racine} ABSENT"); continue; }

                foreach (var mr in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var mf = mr.GetComponent<MeshFilter>();
                    var m = mf == null ? null : mf.sharedMesh;
                    if (m == null) continue;

                    var flags = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
                    bool contribue = (flags & StaticEditorFlags.ContributeGI) != 0;

                    // Aire monde, pour ramener l'échelle d'atlas à des texels
                    // par mètre. L'emprise suffit : on cherche un ordre de
                    // grandeur, pas une aire exacte.
                    var b = mr.bounds.size;
                    float aire = 2f * (b.x * b.y + b.y * b.z + b.x * b.z);

                    var so = mr.lightmapScaleOffset;
                    string densite = "-";
                    if (mr.lightmapIndex >= 0 && mr.lightmapIndex < maps.Length
                        && maps[mr.lightmapIndex].lightmapColor != null && aire > 0.01f)
                    {
                        var tex = maps[mr.lightmapIndex].lightmapColor;
                        float texels = so.x * tex.width * so.y * tex.height;
                        densite = (texels / aire).ToString("F1");
                    }

                    // UV2 : présence, dégénérescence, débordement.
                    var uv2 = m.uv2;
                    string etatUv;
                    if (uv2 == null || uv2.Length == 0)
                        etatUv = "ABSENTS";
                    else
                    {
                        var tri = m.triangles;
                        int degeneres = 0;
                        for (int i = 0; i + 2 < tri.Length; i += 3)
                        {
                            if (tri[i] >= uv2.Length || tri[i + 1] >= uv2.Length
                                || tri[i + 2] >= uv2.Length) continue;
                            var a = uv2[tri[i]];
                            var bb = uv2[tri[i + 1]];
                            var c = uv2[tri[i + 2]];
                            float aire2 = Mathf.Abs((bb.x - a.x) * (c.y - a.y)
                                                    - (c.x - a.x) * (bb.y - a.y)) * 0.5f;
                            if (aire2 < 1e-9f) degeneres++;
                        }
                        int hors = uv2.Count(u => u.x < -0.001f || u.x > 1.001f
                                                  || u.y < -0.001f || u.y > 1.001f);
                        int tris = tri.Length / 3;
                        etatUv = $"{tris} tri, {degeneres} dégénérés "
                                 + $"({(tris > 0 ? 100f * degeneres / tris : 0f):F1} %), "
                                 + $"{hors} sommets hors [0,1]";
                    }

                    sb.AppendLine($"  {mr.name}");
                    sb.AppendLine($"    lightmapIndex  {mr.lightmapIndex}"
                                  + (mr.lightmapIndex < 0 ? "   <- NON CUIT" : ""));
                    sb.AppendLine($"    échelle UV     {so.x:F4} x {so.y:F4}"
                                  + $"   décalage {so.z:F3}, {so.w:F3}");
                    sb.AppendLine($"    texels/m²      {densite}   (consigne 144 = 12 px/unité)");
                    sb.AppendLine($"    emprise        {b.x:F1} x {b.y:F1} x {b.z:F1} m"
                                  + $", ~{aire:F0} m²");
                    sb.AppendLine($"    contribue GI   {contribue}");
                    sb.AppendLine($"    UV2            {etatUv}");
                    sb.AppendLine($"    matériaux      "
                                  + string.Join(", ", mr.sharedMaterials
                                      .Select(x => x == null ? "NUL"
                                              : x.name + " / " + (x.shader == null ? "SANS SHADER" : x.shader.name))));
                }
            }

            // 3. Ce que contiennent réellement les atlas.
            //
            // C'est la mesure qui tranche. Une carte lumineuse sur une salle
            // noire accuse l'albédo ; une carte noire accuse la cuisson. Les
            // deux donnent la même capture d'écran.
            //
            // On passe par un rendu intermédiaire plutôt que par isReadable :
            // rendre l'asset lisible le réimporterait, et un diagnostic ne
            // doit rien changer à ce qu'il observe.
            sb.AppendLine("  ---- contenu des atlas ----");
            for (int i = 0; i < maps.Length; i++)
            {
                var tex = maps[i].lightmapColor;
                if (tex == null) { sb.AppendLine($"    [{i}] aucune texture"); continue; }
                var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
                                                    RenderTextureFormat.ARGBFloat,
                                                    RenderTextureReadWrite.Linear);
                var avant = RenderTexture.active;
                Graphics.Blit(tex, rt);
                RenderTexture.active = rt;
                var lu = new Texture2D(tex.width, tex.height, TextureFormat.RGBAFloat, false, true);
                lu.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                lu.Apply();
                RenderTexture.active = avant;
                RenderTexture.ReleaseTemporary(rt);

                var px = lu.GetPixels();
                double somme = 0; float max = 0f; int noirs = 0;
                foreach (var c in px)
                {
                    float y = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    somme += y; if (y > max) max = y;
                    if (y < 0.01f) noirs++;
                }
                float moy = (float)(somme / px.Length);
                sb.AppendLine($"    [{i}] {tex.width}x{tex.height}  "
                              + $"luminance moyenne {moy:F4}, max {max:F3}, "
                              + $"{100f * noirs / px.Length:F1} % de texels noirs");
                Object.DestroyImmediate(lu);
            }

            // 4. Les lampes : ce qui a été donné à cuire.
            sb.AppendLine("  ---- lampes ----");
            var gl = GameObject.Find("AMPHI/ECLAIRAGE");
            if (gl == null) sb.AppendLine("    groupe ECLAIRAGE absent");
            else
                foreach (var g in gl.GetComponentsInChildren<Light>(true)
                                    .GroupBy(l => $"{l.type} / {l.lightmapBakeType}"))
                {
                    var t = g.ToList();
                    sb.AppendLine($"    {g.Key} : {t.Count} lampes, "
                                  + $"intensité {t.Min(l => l.intensity):F2} à {t.Max(l => l.intensity):F2}, "
                                  + $"portée {t.Min(l => l.range):F1} à {t.Max(l => l.range):F1} m, "
                                  + $"allumées {t.Count(l => l.enabled && l.gameObject.activeInHierarchy)}");
                }

            // 5. L'albédo. Une salle peut être parfaitement éclairée et rester
            // noire si toutes ses matières absorbent la lumière.
            sb.AppendLine("  ---- albédo des matières ----");
            foreach (var mat in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Amphi/Materials" })
                                             .Select(x => AssetDatabase.LoadAssetAtPath<Material>(
                                                 AssetDatabase.GUIDToAssetPath(x)))
                                             .Where(x => x != null)
                                             .OrderBy(x => Lum(x)))
            {
                var c = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.magenta;
                sb.AppendLine($"    {Lum(mat):F3}  {mat.name}"
                              + $"   RVB {c.r:F2} {c.g:F2} {c.b:F2}"
                              + (mat.HasProperty("_EmissionColor")
                                 && mat.GetColor("_EmissionColor").maxColorComponent > 0.01f
                                 ? $"   émissif {mat.GetColor("_EmissionColor").maxColorComponent:F2}" : ""));
            }
            sb.AppendLine("    (un intérieur cuit s'effondre sous ~0,20 : chaque rebond "
                          + "multiplie l'albédo, donc quatre rebonds à 0,10 ne rendent rien)");

            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>
        /// Rend quelques vues fixes de la scène cuite, en PNG.
        /// </summary>
        /// <remarks>
        /// LA LUMINANCE DE L'ATLAS NE DIT PAS SI LA SALLE EST CLAIRE.
        ///
        /// En URP, une carte de lumière non directionnelle stocke la lumière
        /// REÇUE par la surface ; le shader la multiplie ensuite par l'albédo.
        /// Relever l'albédo d'un plafond de 0,28 à 0,70 ne change donc presque
        /// rien à l'atlas — seule la part de rebond bouge — mais éclaircit le
        /// rendu d'un facteur 2,5. Mesurer l'atlas pour juger de la clarté,
        /// c'est lire le mauvais nombre.
        ///
        /// On rend donc des images, et on les regarde.
        ///
        ///   Unity -batchmode -quit -projectPath &lt;projet&gt; \
        ///         -executeMethod AmphiEditor.AmphiDiagEclairage.Vues \
        ///         -logFile diag.log
        /// </remarks>
        [MenuItem("AmphiTraining/9 bis. Rendre des vues de contrôle")]
        public static void Vues()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager
                    .GetActiveScene().path.EndsWith("Amphi.unity"))
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);

            var dossier = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(Application.dataPath), "Vues");
            System.IO.Directory.CreateDirectory(dossier);

            // Les points de vue reprennent ceux des captures du casque, pour
            // qu'on puisse comparer deux images du même endroit.
            var prises = new (string nom, Vector3 oeil, Vector3 cible)[]
            {
                ("01_fond_vers_scene",  new Vector3(0f, 5.2f, -11.5f), new Vector3(0f, 1.6f, 6f)),
                // 02 et 04 replacées le 2026-09-23 : l'œil de 02 était à
                // z = 5, derrière le mur des écrans (image noire), et 04
                // visait le bureau en tournant le dos au pupitre, qui est en
                // x = +2,15 côté Unity (x = -x Blender).
                ("02_scene_vers_salle", new Vector3(0f, 2.2f, 3.0f),   new Vector3(0f, 3.0f, -8f)),
                ("03_allee",            new Vector3(-4.5f, 2.4f, -4f), new Vector3(2f, 1.2f, 4f)),
                ("04_pupitre",          new Vector3(4.2f, 1.9f, -0.6f), new Vector3(2.15f, 1.15f, 2.3f)),
                // Vues rapprochées, à hauteur d'yeux : c'est là que se voient
                // les défauts qu'une vue d'ensemble noie.
                ("05_tablette_de_cote", new Vector3(-7.5f, 1.55f, -2.0f), new Vector3(-2.0f, 1.20f, -1.0f)),
                ("06_tablette_de_pres", new Vector3(-2.2f, 1.45f, -1.2f), new Vector3(-0.2f, 1.05f, -2.2f)),
                ("07_fond_de_salle",    new Vector3(0f, 4.6f, 2.0f),      new Vector3(0f, 3.4f, -13.0f)),
                // Campagne du 2026-09-23 : les deux joues du bureau, vues de
                // biais (on y voyait au travers), la vue d'une personne assise
                // sur le bureau, et la moquette au pied des escaliers (on y
                // voyait une grille millimétrée). Unity x = -x Blender.
                ("08_bureau_joue_centre", new Vector3(1.8f, 1.6f, -0.2f),  new Vector3(-0.2f, 1.0f, 1.5f)),
                ("09_bureau_joue_mur",    new Vector3(-6.4f, 1.6f, -0.2f), new Vector3(-4.5f, 1.0f, 1.5f)),
                ("10_assis_sur_bureau",   new Vector3(-2.35f, 2.12f, 1.47f), new Vector3(-2.35f, 1.5f, -8f)),
                ("11_sol_pied_escalier",  new Vector3(-7.5f, 1.65f, -0.8f), new Vector3(-5.5f, 0.0f, -2.8f)),
                ("12_sol_de_pres",        new Vector3(2.0f, 1.65f, -0.6f),  new Vector3(3.0f, 0.0f, -2.2f)),
            };

            var go = new GameObject("CAM_DIAG");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 75f;          // proche du champ d'un casque
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200f;

            // LA CAMERA DE CONTROLE DOIT VOIR CE QUE VOIT LE CASQUE.
            //
            // Une Camera nue ne passe pas par le post-traitement : sans ces
            // deux lignes, les vues montreraient la salle SANS bloom, sans
            // tonemapping et sans les réglages de couleur, et l'on jugerait
            // sur une image que personne n'aura jamais sous les yeux. Par
            // réflexion, pour ne pas lier ce fichier au paquet URP.
            var tData = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(
                    "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData", false))
                .FirstOrDefault(x => x != null);
            if (tData != null)
            {
                var data = go.GetComponent(tData) ?? go.AddComponent(tData);
                var p = tData.GetProperty("renderPostProcessing");
                if (p != null && p.CanWrite) p.SetValue(data, true);
                cam.allowHDR = true;
            }
            else
            {
                Debug.LogWarning("[Amphi] URP introuvable : les vues sont rendues "
                                 + "SANS post-traitement, elles ne montrent donc "
                                 + "pas ce que le casque affichera.");
            }

            var sb = new StringBuilder();
            sb.AppendLine("[Amphi] VUES DE CONTROLE");
            foreach (var p in prises)
            {
                go.transform.position = p.oeil;
                go.transform.rotation = Quaternion.LookRotation(p.cible - p.oeil);

                var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32,
                                           RenderTextureReadWrite.sRGB);
                rt.antiAliasing = 4;
                cam.targetTexture = rt;
                cam.Render();

                var avant = RenderTexture.active;
                RenderTexture.active = rt;
                var img = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                img.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                img.Apply();
                RenderTexture.active = avant;
                cam.targetTexture = null;

                // La luminance moyenne de l'image : le chiffre que l'atlas ne
                // donnait pas, et celui qui répond à « est-ce sombre ».
                double s = 0;
                foreach (var c in img.GetPixels())
                    s += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                float moy = (float)(s / (rt.width * rt.height));

                var chemin = System.IO.Path.Combine(dossier, p.nom + ".png");
                System.IO.File.WriteAllBytes(chemin, img.EncodeToPNG());
                sb.AppendLine($"  {p.nom}  luminance moyenne {moy:F3}  ->  {chemin}");

                Object.DestroyImmediate(img);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            Object.DestroyImmediate(go);

            Debug.Log(sb.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        static float Lum(Material m)
        {
            if (m == null || !m.HasProperty("_BaseColor")) return -1f;
            var c = m.GetColor("_BaseColor").linear;   // la propriété est en gamma
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }
    }
}
