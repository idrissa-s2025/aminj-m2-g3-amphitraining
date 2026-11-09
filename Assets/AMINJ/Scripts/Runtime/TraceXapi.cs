// TraceXapi.cs : une trace par action, au format xAPI simplifie (acteur, verbe, objet).
//
// Le relais ecrit chaque enonce dans relais/journal/traces-AAAA-MM-JJ.jsonl, horodate.
// Ce sont ces traces qui alimenteront l'evaluation et le debriefing : le groupe choisit
// les verbes AVANT de developper (voir docs/A_CONSTRUIRE.md).
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Aminj
{
    public class TraceXapi : MonoBehaviour
    {
        [Tooltip("Meme adresse que l'AgentRelais.")]
        public string urlRelais = "http://localhost:8787";

        [Tooltip("Identifiant anonyme de l'apprenant pour la seance (jamais un nom reel).")]
        public string acteur = "apprenant-01";

        [Serializable] private class Enonce { public string acteur; public string verbe; public string objet; public string resultat; }

        public void Tracer(string verbe, string objet, string resultat = "")
        {
            StartCoroutine(Envoyer(new Enonce { acteur = acteur, verbe = verbe, objet = objet, resultat = resultat }));
        }

        private IEnumerator Envoyer(Enonce e)
        {
            using (var req = new UnityWebRequest(urlRelais.TrimEnd('/') + "/api/trace", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(e)));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 5;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning($"[Trace] non transmise ({req.error}) : {e.verbe} {e.objet}");
            }
        }
    }
}
