// Réglages du projet pour Meta Quest 3, appliqués par code.
//
// Tout ce qui suit se règle aussi à la souris dans les fenêtres de réglages ;
// le faire ici évite les vingt clics et, surtout, évite d'en oublier un. Les
// valeurs sont celles qu'impose un casque autonome : ARM64, IL2CPP, Vulkan,
// espace linéaire, MSAA 4x, pas de HDR.
//
// La configuration XR (chargeur OpenXR + profil Meta Quest) passe par
// RÉFLEXION, à dessein : si le paquet OpenXR n'est pas encore installé, un
// appel direct empêcherait la compilation de TOUT le dossier Editor, y
// compris le constructeur de scène. Avec la réflexion, le script se contente
// de dire ce qu'il reste à cocher.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace AmphiEditor
{
    public static class AmphiProjectSetup
    {
        const string IdApplication = "com.amphitraining.vr";

        [MenuItem("AmphiTraining/1. Configurer le projet (Quest 3)", false, 10)]
        public static void Configurer()
        {
            Joueur();
            Qualite();
            ConfigurerURP();
            ConfigurerXR();
            AssetDatabase.SaveAssets();
            Debug.Log("[Amphi] projet configuré pour Quest 3. "
                      + "Etape suivante : AmphiTraining / 2.");
        }

        static void Joueur()
        {
            PlayerSettings.companyName = "AmphiTraining";
            PlayerSettings.productName = "AmphiTraining VR";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.gcIncremental = true;
            PlayerSettings.MTRendering = true;
            PlayerSettings.stripEngineCode = true;

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, IdApplication);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(android,
                ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                new[] { GraphicsDeviceType.Vulkan });

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            // Internet OBLIGATOIRE depuis l'écran de présentation : il
            // télécharge les documents en ligne (Drive, Slides, GitHub).
            // Unity l'ajoute d'ordinaire en voyant UnityWebRequest, mais le
            // décalage entre code et manifeste ne se voit qu'une fois dans le
            // casque, au premier téléchargement refusé.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.blitType = AndroidBlitType.Never;  // rendu direct
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            // `androidIsGame` et `startInFullscreen` ne sont plus réglés :
            // dépréciés en 6.6 au profit d'appCategory et de
            // requestedVisibleInsets, et sans effet sur un casque, qui est
            // toujours en plein écran.

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Android, BuildTarget.Android);
        }

        static void Qualite()
        {
            // Un seul niveau de qualité sert : celui du casque. On règle celui
            // qui est actif, pour ne pas dépendre de l'ordre de la liste.
            QualitySettings.antiAliasing = 4;          // MSAA 4x : le minimum en VR

            // LE FILTRAGE ANISOTROPE, SANS LEQUEL LE NIVEAU 8 NE SERT A RIEN.
            //
            // Chaque texture est importée avec `anisoLevel = 8`, mais ce
            // niveau n'est LU que si le réglage global l'autorise. Laissé à
            // `Disable` — la valeur par défaut d'un projet neuf — il annule
            // les neuf réglages d'import d'un seul coup, sans rien signaler.
            //
            // C'est précisément ce qui se voit au sol et au plafond d'un
            // amphithéâtre : ce sont les surfaces vues en fuyante, celles que
            // l'anisotropie sauve et que le mip-mapping seul transforme en
            // bouillie à trois mètres.
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            QualitySettings.vSyncCount = 0;            // le compositeur XR cadence
            QualitySettings.shadows = ShadowQuality.HardOnly;
            QualitySettings.shadowResolution = ShadowResolution.Low;
            QualitySettings.shadowDistance = 18f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            QualitySettings.softParticles = false;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.billboardsFaceCameraPosition = true;
            QualitySettings.lodBias = 1.4f;
            QualitySettings.maximumLODLevel = 0;
            QualitySettings.streamingMipmapsActive = true;
        }

        /// <summary>
        /// Applique les réglages de casque à TOUS les assets de pipeline du
        /// projet, et dit lequel chaque niveau de qualité utilise réellement.
        /// </summary>
        /// <remarks>
        /// REGLER `defaultRenderPipeline` NE SUFFIT PAS, ET C'EST UN PIEGE.
        ///
        /// Un projet Unity 6 neuf arrive avec des niveaux de qualité qui ont
        /// CHACUN leur propre asset URP : « Mobile » pointe sur
        /// Mobile_RPAsset, « PC » sur PC_RPAsset. Sur Android, c'est le niveau
        /// Mobile qui s'applique, donc Mobile_RPAsset, et non l'asset désigné
        /// comme pipeline par défaut.
        ///
        /// On réglait donc soigneusement PipelineQuest pendant que le casque
        /// utilisait Mobile_RPAsset, livré à renderScale 0,8 et MSAA 1. Le
        /// casque l'a mesuré sans ambiguïté : 1344 x 1408 px par oeil, facteur
        /// effectif 0,80, MSAA 0x — alors que le journal affichait
        /// « msaaSampleCount = 4 » à chaque construction.
        ///
        /// Rien ne le signalait, parce que les deux affirmations étaient
        /// vraies : l'asset était bien réglé, il n'était simplement pas celui
        /// qui servait. Les rendus hors ligne, eux, passaient par le pipeline
        /// par défaut et montraient l'amélioration attendue. C'est le genre
        /// d'écart qu'on ne voit qu'en mesurant SUR L'APPAREIL.
        ///
        /// On règle donc tous les assets, quel que soit celui qui sera choisi.
        /// </remarks>
        static void ReglerToutesLesQualites()
        {
            var vus = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:RenderPipelineAsset"))
            {
                var chemin = AssetDatabase.GUIDToAssetPath(guid);
                var a = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(chemin);
                if (a == null) continue;
                ReglerAssetURP(a);
                vus.Add(a.name);
            }

            // Et on DIT quel asset chaque niveau de qualité utilise : c'est la
            // information qui manquait pour comprendre l'écart.
            int courant = QualitySettings.GetQualityLevel();
            var noms = QualitySettings.names;
            for (int i = 0; i < noms.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                var rp = QualitySettings.renderPipeline;
                Debug.Log($"[Amphi] qualité « {noms[i]} » -> "
                          + (rp != null ? rp.name : "pipeline par défaut")
                          + $", MSAA {QualitySettings.antiAliasing}x");
            }
            QualitySettings.SetQualityLevel(courant, false);
            Debug.Log($"[Amphi] {vus.Count} asset(s) de pipeline réglé(s) : "
                      + string.Join(", ", vus));
        }

        /// <summary>
        /// Pose sur l'asset URP les réglages qu'impose un casque, et RELIT.
        /// </summary>
        /// <remarks>
        /// Par réflexion : les propriétés de l'asset URP ont changé de nom et
        /// d'accessibilité d'une version à l'autre, et une erreur de
        /// compilation ici bloquerait toute la chaîne.
        ///
        /// On relit chaque valeur après l'avoir écrite. Une propriété posée
        /// n'est pas une propriété appliquée : c'est exactement ainsi que le
        /// chargeur OpenXR est resté non assigné pendant plusieurs sessions,
        /// un `?.Invoke` sur une méthode introuvable ne faisant rien sans
        /// rien dire.
        /// </remarks>
        static void ReglerAssetURP(RenderPipelineAsset asset)
        {
            if (asset == null) return;
            var t = asset.GetType();
            var voulu = new (string nom, object valeur)[]
            {
                // 4x est le minimum en VR. Sans lui, toute arête fine
                // scintille à chaque mouvement de tête.
                ("msaaSampleCount", 4),
                // HDR ACTIF, et c'est un revirement assumé.
                //
                // Je l'avais coupé pour la bande passante du casque, ce qui se
                // défend tant que la salle sort telle quelle. Mais la session
                // distante a ajouté un post-traitement — bloom, tonemapping,
                // réglages de couleur — et ces trois effets travaillent sur ce
                // qui DEPASSE 1. Sans HDR, la caméra écrête tout à 1 avant
                // qu'ils ne s'exécutent : le bloom n'a plus rien à faire
                // déborder, et un tonemapping ACES appliqué à du LDR ne fait
                // que redescendre ce qui est déjà plat.
                //
                // Les deux réglages se contredisaient donc en silence, et dans
                // le mauvais ordre : les réglages du projet passent à l'étape
                // 1, la scène à l'étape 2. Le post-traitement aurait été posé,
                // mesurable, correct sur le papier, et sans effet à l'image.
                //
                // `Camera.allowHDR` ne suffit pas : en URP il est subordonné à
                // ce drapeau-ci. C'est celui-là qui décide.
                //
                // Si le budget image dépasse 13,8 ms, l'ordre des leviers est
                // le bloom d'abord (downscale, maxIterations), celui-ci
                // ensuite.
                ("supportsHDR", true),

                // LE FACTEUR DE RENDU : 1,23, ET C'EST UN CHIFFRE, PAS UN GOUT.
                //
                // Un Quest 3 rend par défaut 1680 x 1760 pixels par oeil, alors
                // que sa dalle en affiche 2064 x 2208. A 1,0, on calcule donc
                // une image PLUS PETITE que l'écran et le compositeur l'étire :
                // c'est une perte de netteté que ni l'anticrénelage ni les
                // textures ne peuvent rattraper, puisqu'elle est en aval d'eux.
                //
                // 2064 / 1680 = 1,229. A cette valeur, un pixel calculé vaut un
                // pixel de dalle, ce qui est la définition même d'une image
                // nette. Meta le documente comme « une augmentation
                // significative de la clarté », et va jusqu'à refuser du
                // Horizon Store les applications dont le facteur est trop bas
                // (VRC.Quest.Performance.4).
                //
                // Le surcoût est de 51 % de pixels. C'est le rendu fovéal qui
                // le paie, en abaissant la finesse là où l'oeil ne voit pas.
                ("renderScale", 1.23f),
            };
            foreach (var (nom, valeur) in voulu)
            {
                var p = t.GetProperty(nom, BindingFlags.Public | BindingFlags.NonPublic
                                           | BindingFlags.Instance);
                if (p == null || !p.CanWrite)
                {
                    Debug.LogWarning($"[Amphi] URP : propriété « {nom} » absente ou "
                                     + "non modifiable dans cette version. A vérifier "
                                     + "à la main dans l'asset de pipeline.");
                    continue;
                }
                try { p.SetValue(asset, valeur); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Amphi] URP : {nom} refusé ({e.Message})");
                    continue;
                }
                var relu = p.GetValue(asset);
                if (!Equals(relu, valeur))
                    Debug.LogWarning($"[Amphi] URP : {nom} vaut {relu} alors qu'on "
                                     + $"a demandé {valeur}.");
                else
                    Debug.Log($"[Amphi] URP : {nom} = {relu}");
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Crée et assigne un pipeline URP si le projet n'en a pas.
        ///
        /// Sans pipeline assigné, le projet reste en rendu intégré : les
        /// matériaux URP s'y affichent en magenta, et le batcher SRP — qui
        /// vaut plusieurs images par seconde sur casque — ne tourne pas. On
        /// passe par la réflexion pour que ce fichier compile même si le
        /// paquet URP n'est pas installé.
        /// </summary>
        static void ConfigurerURP()
        {
            // UN PIPELINE DEJA LA N'EST PAS UN PIPELINE DEJA REGLE.
            //
            // Ce raccourci coûtait la netteté de toute la salle. On sortait
            // ici dès qu'un asset existait, donc les réglages de casque
            // n'étaient appliqués qu'à celui qu'on venait de créer, jamais à
            // celui qu'on trouvait. L'asset gardait alors les valeurs par
            // défaut d'Unity, MSAA compris — c'est-à-dire AUCUN
            // anticrénelage, alors que l'en-tête de ce fichier annonce 4x.
            //
            // En URP, `QualitySettings.antiAliasing` ne décide de rien : c'est
            // l'asset qui tranche. Régler l'un en croyant régler l'autre est
            // le genre d'erreur qui ne se voit pas à l'écran et saute aux yeux
            // dans un casque, où 5 100 lattes de plafond et des bardages à
            // claire-voie fourmillent à chaque mouvement de tête.
            if (GraphicsSettings.defaultRenderPipeline != null)
            {
                Debug.Log("[Amphi] pipeline de rendu déjà assigné : "
                          + GraphicsSettings.defaultRenderPipeline.name);
                ReglerToutesLesQualites();
                return;
            }
            try
            {
                var tData = Type.GetType("UnityEngine.Rendering.Universal."
                    + "UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
                var tAsset = Type.GetType("UnityEngine.Rendering.Universal."
                    + "UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime");
                if (tData == null || tAsset == null)
                {
                    RappelURP();
                    return;
                }
                const string dossier = "Assets/Amphi/Rendu";
                System.IO.Directory.CreateDirectory(dossier);
                var data = ScriptableObject.CreateInstance(tData);
                AssetDatabase.CreateAsset(data, dossier + "/RenduQuest.asset");
                var create = tAsset.GetMethod("Create",
                    BindingFlags.Public | BindingFlags.Static);
                var asset = create?.Invoke(null, new object[] { data })
                            as RenderPipelineAsset;
                if (asset == null) { RappelURP(); return; }
                AssetDatabase.CreateAsset(asset, dossier + "/PipelineQuest.asset");
                GraphicsSettings.defaultRenderPipeline = asset;
                QualitySettings.renderPipeline = asset;
                ReglerToutesLesQualites();
                AssetDatabase.SaveAssets();
                Debug.Log("[Amphi] pipeline URP créé et assigné.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Amphi] URP non configuré : " + e.Message);
                RappelURP();
            }
        }

        static void RappelURP()
        {
            Debug.LogWarning(
                "[Amphi] pipeline URP à créer à la main :\n"
                + "  Assets > Create > Rendering > URP Asset (with Universal Renderer)\n"
                + "  puis Project Settings > Graphics > Default Render Pipeline.\n"
                + "  Sans lui, la scène est construite en shaders Standard : "
                + "correct, mais moins rapide sur casque.");
        }

        // ------------------------------------------------------------------ XR
        static void ConfigurerXR()
        {
            try
            {
                if (!AssignerChargeurOpenXR())
                {
                    Rappel();
                    return;
                }
                ActiverFonctions();

                // On ne croit pas l'assignation sur parole : on relit.
                if (!ChargeurEnPlace())
                {
                    Debug.LogWarning("[Amphi] le chargeur OpenXR ne s'est pas "
                                     + "inscrit : l'APK se lancerait en 2D, écran "
                                     + "noir dans le casque.");
                    Rappel();
                }
            }
            catch (Exception e)
            {
                // le type et la pile, pas seulement le message : « Ambiguous
                // match found » tout seul ne dit pas quelle réflexion a
                // échoué, et c'est une demi-heure de perdue.
                Debug.LogWarning("[Amphi] configuration XR automatique impossible : "
                                 + e.GetType().Name + " : " + e.Message + "\n" + e);
                Rappel();
            }
        }

        static void Rappel()
        {
            Debug.LogWarning(
                "[Amphi] à faire à la main (30 secondes) :\n"
                + "  Edit > Project Settings > XR Plug-in Management, onglet Android :\n"
                + "    cocher OpenXR\n"
                + "  puis, sous OpenXR > Android :\n"
                + "    Interaction Profiles : ajouter « Oculus Touch Controller Profile »\n"
                + "    OpenXR Feature Groups : cocher « Meta Quest Support »\n"
                + "    Render Mode : Single Pass Instanced");
        }

        /// <summary>
        /// Coche « OpenXR » dans XR Plug-in Management, onglet Android.
        ///
        /// C'EST LE REGLAGE QUI DECIDE SI LE CASQUE AFFICHE QUELQUE CHOSE.
        /// Sans chargeur assigné pour Android, l'APK se construit sans une
        /// erreur, s'installe, se lance, et reste noir : Unity le démarre en
        /// application 2D. Le défaut ne se voit qu'une fois le casque sur la
        /// tête, ce qui en fait le plus cher de tous.
        ///
        /// POURQUOI PAR REFLEXION. XR Management n'est pas une dépendance de
        /// ce projet : le paquet peut être absent (visite à l'écran, sans
        /// casque), et une référence directe empêcherait alors de compiler.
        ///
        /// Le piège : `CreateDefaultManagerSettingsForBuildTarget` est une
        /// méthode D'INSTANCE, pas statique. Cherchée en `Static`, la
        /// réflexion rend `null`, `?.Invoke` ne fait rien sans rien dire, et
        /// l'assignation échouait en silence. C'était le cas ici, avec un
        /// `m_SettingsPerBuildTarget: []` dans l'asset pour seule trace. On
        /// passe donc par l'instance, obtenue par `GetOrCreate` (interne).
        /// </summary>
        static bool AssignerChargeurOpenXR()
        {
            var tSettings = Type.GetType(
                "UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget, "
                + "Unity.XR.Management.Editor");
            var tStore = Type.GetType(
                "UnityEditor.XR.Management.Metadata.XRPackageMetadataStore, "
                + "Unity.XR.Management.Editor");
            if (tSettings == null || tStore == null) return false;

            const BindingFlags Tous = BindingFlags.Public | BindingFlags.NonPublic
                                      | BindingFlags.Static | BindingFlags.Instance;

            // l'objet qui tient les réglages de TOUTES les plateformes
            object par = tSettings.GetMethod("GetOrCreate", Tous)?.Invoke(null, null);
            if (par == null)
            {
                // repli : l'asset existe peut-être déjà sans être référencé
                var tryFind = tSettings.GetMethod("TryFindSettingsAsset", Tous);
                if (tryFind != null)
                {
                    var p = new object[] { null };
                    if (tryFind.Invoke(null, p) is bool trouve && trouve) par = p[0];
                }
            }
            if (par == null) return false;

            // les réglages d'Android, et son gestionnaire de chargeurs
            var aManager = tSettings.GetMethod("HasManagerSettingsForBuildTarget", Tous);
            bool complet = aManager?.Invoke(par, new object[] { BuildTargetGroup.Android })
                           is bool b2 && b2;
            if (!complet)
                tSettings.GetMethod("CreateDefaultManagerSettingsForBuildTarget", Tous)
                         ?.Invoke(par, new object[] { BuildTargetGroup.Android });

            var manager = tSettings.GetMethod("ManagerSettingsForBuildTarget", Tous)
                                   ?.Invoke(par, new object[] { BuildTargetGroup.Android });
            if (manager == null) return false;

            var assign = tStore.GetMethod("AssignLoader",
                                          BindingFlags.Public | BindingFlags.Static);
            bool ok = assign?.Invoke(null, new object[]
            {
                manager, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android
            }) is bool b && b;

            if (ok)
            {
                EditorUtility.SetDirty((UnityEngine.Object)par);
                AssetDatabase.SaveAssets();
                Debug.Log("[Amphi] chargeur OpenXR assigné pour Android.");
            }
            return ok;
        }

        /// <summary>
        /// Relit l'asset pour confirmer que le chargeur est bien là.
        ///
        /// L'assignation peut rendre `true` sans que rien ne soit écrit :
        /// c'est ce contrôle-là, et non la valeur de retour, qui distingue un
        /// APK VR d'un APK noir. On le fait donc dire oui ou non, en clair.
        /// </summary>
        static bool ChargeurEnPlace()
        {
            var tStore = Type.GetType(
                "UnityEditor.XR.Management.Metadata.XRPackageMetadataStore, "
                + "Unity.XR.Management.Editor");
            var est = tStore?.GetMethod("IsLoaderAssigned",
                                        BindingFlags.Public | BindingFlags.Static,
                                        null,
                                        new[] { typeof(string), typeof(BuildTargetGroup) },
                                        null);
            return est?.Invoke(null, new object[]
            {
                "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Android
            }) is bool b && b;
        }

        /// <summary>
        /// Active le support Meta Quest et le profil de manettes Oculus Touch.
        /// Sans le profil, les manettes ne renvoient rien : ni joystick, ni
        /// bouton, et la téléportation reste muette sans le moindre message.
        /// </summary>
        static void ActiverFonctions()
        {
            var tOpenXR = Type.GetType(
                "UnityEngine.XR.OpenXR.OpenXRSettings, Unity.XR.OpenXR");
            if (tOpenXR == null) { Rappel(); return; }
            var get = tOpenXR.GetMethod("GetSettingsForBuildTargetGroup",
                                        BindingFlags.Public | BindingFlags.Static);
            var settings = get?.Invoke(null, new object[] { BuildTargetGroup.Android });
            if (settings == null) { Rappel(); return; }

            // `GetMethod("GetFeatures", Type.EmptyTypes)` lève « Ambiguous match
            // found » : OpenXRSettings a CINQ surcharges de GetFeatures, dont
            // `GetFeatures()` et la générique `GetFeatures<TFeature>()`. Toutes
            // deux sont sans paramètre, et une liste de types ne les départage
            // pas. On choisit donc la méthode à la main.
            var mGet = settings.GetType().GetMethods()
                               .FirstOrDefault(m => m.Name == "GetFeatures"
                                                    && !m.IsGenericMethod
                                                    && m.GetParameters().Length == 0);
            var features = mGet?.Invoke(settings, null) as Array;
            if (features == null) { Rappel(); return; }

            // « Foveated » rejoint la liste : c'est ce qui PAIE la montée en
            // définition. Le rendu fovéal baisse la finesse en périphérie du
            // champ, là où l'oeil ne distingue rien, et rend le budget au
            // centre. Sans lui, passer de 1680 à 2064 px par oeil coûterait
            // 51 % de pixels en plus sans contrepartie.
            string[] voulus = { "MetaQuest", "OculusTouch", "Foveated" };
            int actives = 0;
            foreach (var f in features)
            {
                if (f == null) continue;
                string nom = f.GetType().Name;
                if (!voulus.Any(v => nom.Contains(v))) continue;
                // même prudence : une sous-classe qui redéclare « enabled »
                // rendrait GetProperty ambigu. La première trouvée est celle
                // du type le plus dérivé, et c'est celle qu'on veut.
                var prop = f.GetType().GetProperties()
                            .FirstOrDefault(p => p.Name == "enabled"
                                                 && p.PropertyType == typeof(bool)
                                                 && p.CanWrite);
                if (prop == null) continue;
                prop.SetValue(f, true);
                EditorUtility.SetDirty((UnityEngine.Object)f);
                actives++;
            }
            // rendu stéréo en une passe : sur Quest, c'est le réglage qui fait
            // gagner le plus, en une case à cocher
            var rm = settings.GetType().GetProperties()
                             .FirstOrDefault(p => p.Name == "renderMode" && p.CanWrite);
            if (rm != null && rm.PropertyType.IsEnum)
            {
                var v = Enum.GetNames(rm.PropertyType)
                            .FirstOrDefault(n => n.Contains("SinglePassInstanced"));
                if (v != null) rm.SetValue(settings, Enum.Parse(rm.PropertyType, v));
            }

            // L'API DE FOVEATION : « SRP Foveation » et non « Legacy ».
            //
            // Unity 6 applique alors la fovéation passe par passe, en sautant
            // celles où elle n'a pas de sens. La documentation la recommande
            // pour tout projet Unity 6, le mode hérité n'existant que pour
            // 2022 et antérieurs.
            var fov = settings.GetType().GetProperties()
                              .FirstOrDefault(p => p.Name.ToLower().Contains("foveat")
                                                   && p.PropertyType.IsEnum && p.CanWrite);
            if (fov != null)
            {
                var v = Enum.GetNames(fov.PropertyType)
                            .FirstOrDefault(n => n.Contains("SRP") || n.Contains("Srp"));
                if (v != null)
                {
                    fov.SetValue(settings, Enum.Parse(fov.PropertyType, v));
                    Debug.Log($"[Amphi] OpenXR : fovéation = {fov.GetValue(settings)}");
                }
                else Debug.LogWarning("[Amphi] OpenXR : aucune valeur « SRP » pour "
                                      + $"« {fov.Name} » dans cette version.");
            }
            else Debug.LogWarning("[Amphi] OpenXR : réglage de fovéation introuvable. "
                                  + "A cocher à la main : XR Plug-in Management > "
                                  + "OpenXR > Foveated Rendering API = SRP Foveation.");

            EditorUtility.SetDirty((UnityEngine.Object)settings);
            // en mode batch, personne ne sauvegarde à notre place : sans cet
            // appel, les cases cochées ne survivent pas à la fermeture
            AssetDatabase.SaveAssets();
            Debug.Log($"[Amphi] OpenXR : {actives} fonctions activées "
                      + "(Meta Quest + Oculus Touch).");
            if (actives < 2) Rappel();
        }
    }
}
