// Installation des paquets XR, à la version que VOTRE éditeur sait compiler.
//
// POURQUOI NE PAS EPINGLER OPENXR DANS LE MANIFESTE. Un numéro de version
// écrit en dur suppose une version d'Unity. Sur un éditeur plus récent, le
// paquet trop ancien ne compile plus — et l'erreur ne vient même pas de votre
// code :
//
//   PackageCache\com.unity.xr.openxr@…\Runtime\OpenXRProjectValidation.cs:
//   error CS0619: 'Object.GetInstanceID()' is obsolete: 'Use GetEntityId'
//
// Unity 6.6 a fait de ces appels des erreurs dures ; OpenXR 1.14 les employait
// encore. Aucun réglage ne rattrape cela : il faut le paquet correspondant à
// l'éditeur. `Client.Add` SANS numéro de version demande exactement cela —
// la plus récente que cet éditeur accepte.
//
// OpenXR n'est nécessaire QUE pour le casque. Sans lui, la scène se construit
// et se parcourt au clavier : les appels XR de ce projet passent par
// réflexion, précisément pour que son absence n'empêche rien.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace AmphiEditor
{
    public static class AmphiPaquets
    {
        // Sans numéro : c'est tout l'intérêt.
        static readonly string[] Requis =
        {
            "com.unity.xr.openxr",
            "com.unity.xr.management",
        };

        [MenuItem("AmphiTraining/0. Installer les paquets XR", false, 0)]
        public static void Installer()
        {
            var file = new Queue<string>(Requis);
            Suivant(file);
        }

        static void Suivant(Queue<string> file)
        {
            if (file.Count == 0)
            {
                Debug.Log("[Amphi] paquets XR installés. Etape suivante : "
                          + "AmphiTraining / 1.");
                return;
            }
            string nom = file.Dequeue();
            Debug.Log($"[Amphi] installation de {nom} (version choisie par l'éditeur)…");
            AddRequest req = Client.Add(nom);
            void Attendre()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= Attendre;
                if (req.Status == StatusCode.Success)
                    Debug.Log($"[Amphi] {req.Result.name} {req.Result.version} installé");
                else
                    Debug.LogError($"[Amphi] échec pour {nom} : {req.Error?.message}");
                Suivant(file);
            }
            EditorApplication.update += Attendre;
        }

        [MenuItem("AmphiTraining/Diagnostic des paquets", false, 1)]
        public static void Diagnostic()
        {
            var req = Client.List(true);
            void Attendre()
            {
                if (!req.IsCompleted) return;
                EditorApplication.update -= Attendre;
                if (req.Status != StatusCode.Success)
                {
                    Debug.LogError("[Amphi] " + req.Error?.message);
                    return;
                }
                var lignes = new List<string> { "[Amphi] paquets du projet :" };
                foreach (var p in req.Result)
                {
                    if (!p.name.StartsWith("com.unity.xr")
                        && !p.name.Contains("inputsystem")
                        && !p.name.Contains("render-pipelines")) continue;
                    lignes.Add($"  {p.name,-42} {p.version}");
                }
                lignes.Add($"  éditeur : {Application.unityVersion}");
                Debug.Log(string.Join("\n", lignes));
            }
            EditorApplication.update += Attendre;
        }
    }
}
