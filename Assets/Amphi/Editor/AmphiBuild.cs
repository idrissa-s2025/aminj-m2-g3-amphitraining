// Fabrication de l'APK, en un appel — depuis le menu ou en ligne de commande.
//
//   Unity -batchmode -quit -projectPath unity/AmphiTrainingVR \
//         -executeMethod AmphiEditor.AmphiBuild.Quest
//
// Le fichier sort dans Builds/AmphiTrainingVR.apk. Pour l'envoyer au casque :
//
//   adb install -r Builds/AmphiTrainingVR.apk
//
// Une passe de vérification précède la construction : elle refuse de produire
// un APK dont on sait déjà qu'il sera inutilisable (scène absente, éclairage
// non cuit, chargeur XR non assigné). Découvrir cela le casque sur la tête
// coûte bien plus cher que dix lignes de contrôle.

using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AmphiEditor
{
    public static class AmphiBuild
    {
        const string Scene = "Assets/Amphi/Scenes/Amphi.unity";
        const string SortieRelative = "Builds/AmphiTrainingVR.apk";

        /// <summary>
        /// Chemin ABSOLU de l'APK, et non relatif.
        /// </summary>
        /// <remarks>
        /// Unity fait du dossier de projet son répertoire courant — presque
        /// toujours. « Presque » ne suffit pas quand l'étape dure une demi-heure
        /// et qu'un chemin relatif mal résolu fait sortir l'APK dans un dossier
        /// que personne ne pense à regarder. On résout une fois, depuis
        /// Application.dataPath, qui désigne toujours Assets/.
        /// </remarks>
        static string Sortie =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", SortieRelative));

        [MenuItem("AmphiTraining/4. Fabriquer l'APK Quest", false, 40)]
        public static void Quest()
        {
            try
            {
                QuestSansSortie();
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("[Amphi] " + e.Message);
                if (Application.isBatchMode) EditorApplication.Exit(2);
            }
        }

        /// <summary>
        /// Même chose, mais SANS quitter l'éditeur : c'est cette variante
        /// qu'appelle la chaîne complète, qui a encore des étapes après.
        /// </summary>
        public static void QuestSansSortie()
        {
            if (!Verifier(out string probleme))
                throw new Exception("build refusé : " + probleme);

            string apk = Sortie;
            string dossier = Path.GetDirectoryName(apk);
            Directory.CreateDirectory(dossier);
            Debug.Log($"[Amphi] dossier de sortie : {dossier}");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = apk,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.CompressWithLz4HC,
            };
            var rapport = BuildPipeline.BuildPlayer(options);
            var s = rapport.summary;
            Debug.Log($"[Amphi] build {s.result} — {s.totalSize / (1024f * 1024f):F1} Mo "
                      + $"en {s.totalTime.TotalSeconds:F0} s -> {apk}");
            if (s.result != BuildResult.Succeeded)
            {
                // Le résumé ne dit que « Failed ». Les messages utiles sont
                // dans les étapes du rapport ; sans eux, il faut fouiller un
                // journal de dix mille lignes.
                foreach (var etape in rapport.steps)
                    foreach (var msg in etape.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Debug.LogError($"[Amphi] build, « {etape.name} » : {msg.content}");
                throw new Exception($"BuildPlayer a rendu {s.result} "
                                    + $"({s.totalErrors} erreurs)");
            }
            if (!File.Exists(apk))
                throw new Exception($"build annoncé réussi mais {apk} n'existe pas");
        }

        [MenuItem("AmphiTraining/Vérifier la scène avant build", false, 41)]
        public static void VerifierMenu()
        {
            Debug.Log(Verifier(out string p) ? "[Amphi] scène prête pour le casque."
                                             : "[Amphi] " + p);
        }

        /// <summary>
        /// Les mêmes contrôles, exposés pour être passés AVANT la cuisson.
        /// Découvrir après quarante minutes de calcul que la plateforme active
        /// n'est pas Android, c'est perdre la seule étape longue de la chaîne.
        /// </summary>
        public static bool PeutConstruire(out string probleme) => Verifier(out probleme);

        static bool Verifier(out string probleme)
        {
            probleme = null;
            if (!File.Exists(Scene))
            {
                probleme = "la scène n'existe pas — lancez AmphiTraining / 2.";
                return false;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                probleme = "la plateforme active n'est pas Android — "
                           + "lancez AmphiTraining / 1.";
                return false;
            }
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                probleme = "architecture cible différente d'ARM64 : le Quest 3 "
                           + "refusera l'APK.";
                return false;
            }
            if (!EditorBuildSettings.scenes.Any(s => s.path == Scene && s.enabled))
            {
                probleme = "la scène n'est pas cochée dans Build Settings.";
                return false;
            }
            // Le module Android est cherché SUR LE DISQUE, et non par
            // BuildPipeline.IsBuildTargetSupported : cette méthode a changé
            // d'accessibilité entre deux versions d'éditeur, et une erreur de
            // compilation coûte plus cher ici qu'un test de dossier.
            string moteur = Path.Combine(EditorApplication.applicationContentsPath,
                                         "PlaybackEngines", "AndroidPlayer");
            if (!Directory.Exists(moteur))
            {
                probleme = "le module « Android Build Support » n'est pas "
                           + $"installé pour cet éditeur ({moteur} est absent). "
                           + "Unity Hub > Installs > roue dentée > Add modules > "
                           + "Android Build Support, avec OpenJDK et "
                           + "Android SDK & NDK Tools.";
                return false;
            }
            var lighting = Path.Combine(Path.GetDirectoryName(Scene) ?? "",
                                        Path.GetFileNameWithoutExtension(Scene));
            if (!Directory.Exists(lighting))
                Debug.LogWarning("[Amphi] éclairage non cuit : la salle sera plate et "
                                 + "sombre. Lancez AmphiTraining / 3 avant la démo.");
            return true;
        }
    }
}
