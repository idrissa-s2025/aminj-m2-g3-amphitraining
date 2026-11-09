// AgentRelais.cs : la brique OpenAI du socle, en version claire.
//
// Le casque n'appelle jamais OpenAI : il appelle le relais du depot (relais/serveur.mjs),
// qui detient la cle, applique un plafond d'appels et journalise les echanges.
// Ce composant envoie une question et un contexte, et rend la reponse texte.
//
// Ce qu'il NE fait pas, et que le groupe doit concevoir : decider QUAND l'agent parle,
// ce qu'il a le droit de dire (relais/consignes_agent.md), la voix, l'incarnation.
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Aminj
{
    public class AgentRelais : MonoBehaviour
    {
        [Tooltip("Adresse du relais. Dans l'editeur : http://localhost:8787. " +
                 "Dans le casque : l'adresse IP du PC qui lance le relais, ex. http://192.168.1.20:8787")]
        public string urlRelais = "http://localhost:8787";

        [Tooltip("Delai maximal d'une reponse, en secondes.")]
        public int delai = 30;

        [Serializable] private class Requete { public string question; public string contexte; }
        [Serializable] private class Reponse { public string reponse; public string erreur; }

        /// <summary>Pose une question a l'agent. Le rappel recoit la reponse, ou un message d'erreur lisible.</summary>
        public void Demander(string question, string contexte, Action<string> rappel)
        {
            StartCoroutine(Envoyer(question, contexte, rappel));
        }

        private IEnumerator Envoyer(string question, string contexte, Action<string> rappel)
        {
            var corps = JsonUtility.ToJson(new Requete { question = question, contexte = contexte ?? "" });
            using (var req = new UnityWebRequest(urlRelais.TrimEnd('/') + "/api/chat", "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(corps));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = delai;
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.ConnectionError)
                {
                    rappel?.Invoke("Relais injoignable : lancer « node relais/serveur.mjs » et verifier l'adresse.");
                    yield break;
                }
                var r = JsonUtility.FromJson<Reponse>(req.downloadHandler.text ?? "{}");
                rappel?.Invoke(!string.IsNullOrEmpty(r?.reponse) ? r.reponse : (r?.erreur ?? req.error));
            }
        }
    }
}
