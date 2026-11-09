// PreparerSocle.cs : prepare la scene de l'amphitheatre pour le socle AMINJ.
//
// Menu AMINJ > 1. Preparer la scene du socle :
//   - ouvre Assets/Amphi/Scenes/Amphi.unity (salle, eclairage cuit, robots, ecran),
//   - retire les composants dont le script n'est pas fourni dans le socle (agents d'origine),
//   - ajoute l'objet « Socle AMINJ » : agent par le relais, traces xAPI, question du public,
//   - autorise les requetes HTTP vers le relais du reseau local, et enregistre.
// Menu AMINJ > 2. Verifier le socle.
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aminj.Amphi.Editeur
{
    public static class PreparerSocle
    {
        private const string Scene = "Assets/Amphi/Scenes/Amphi.unity";

        [MenuItem("AMINJ/1. Preparer la scene du socle")]
        public static void Preparer()
        {
            var scene = EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);

            int retires = 0;
            foreach (var racine in scene.GetRootGameObjects())
                foreach (var t in racine.GetComponentsInChildren<Transform>(true))
                    retires += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);

            var ancien = GameObject.Find("Socle AMINJ");
            if (ancien) Object.DestroyImmediate(ancien);
            var socle = new GameObject("Socle AMINJ");
            var q = socle.AddComponent<QuestionDuPublic>();
            q.agent = socle.AddComponent<AgentRelais>();
            q.trace = socle.AddComponent<TraceXapi>();

            // Bulle face au point de vue de l'orateur, a trois metres
            var camera = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            var origine = camera ? camera.transform : socle.transform;
            var bulle = new GameObject("Bulle du public").AddComponent<TextMeshPro>();
            bulle.transform.SetParent(socle.transform, false);
            bulle.transform.position = origine.position + Vector3.ProjectOnPlane(origine.forward, Vector3.up).normalized * 3f + Vector3.up * 0.4f;
            bulle.transform.rotation = Quaternion.LookRotation(bulle.transform.position - origine.position, Vector3.up);
            bulle.fontSize = 2f;
            bulle.alignment = TextAlignmentOptions.Center;
            bulle.rectTransform.sizeDelta = new Vector2(2.4f, 0.8f);
            bulle.text = "Q : une question du public";
            q.bulle = bulle;

            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            Debug.Log($"[AMINJ] Scene preparee : {retires} composants d'origine retires, socle ajoute.");
        }

        [MenuItem("AMINJ/2. Verifier le socle")]
        public static bool Verifier()
        {
            var manquants = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(g => g.scene.IsValid())
                .Sum(g => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(g));
            bool ok = manquants == 0 && Object.FindFirstObjectByType<QuestionDuPublic>();
            Debug.Log($"[AMINJ] Verification : {manquants} scripts manquants, " + (ok ? "socle complet." : "socle incomplet."));
            return ok;
        }

        // Unity -batchmode -executeMethod Aminj.Amphi.Editeur.PreparerSocle.EnLot
        public static void EnLot()
        {
            Preparer();
            EditorApplication.Exit(Verifier() ? 0 : 1);
        }
    }
}
