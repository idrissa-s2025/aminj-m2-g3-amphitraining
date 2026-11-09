// QuestionDuPublic.cs : la demonstration de la chaine agent dans l'amphitheatre.
//
// Touche Q (clavier, dans l'editeur) ou appel de DemanderUneQuestion() : le resume de
// l'expose part au relais, l'agent formule UNE question d'auditeur, elle s'affiche au-dessus
// du public et une trace xAPI est ecrite.
//
// Ce n'est PAS un public : il n'y a ni roles, ni reactions, ni moment choisi pour intervenir.
// A construire par le groupe (docs/A_CONSTRUIRE.md) : qui parle et quand, les roles des dix
// robots, ce qui est observe de l'oral, les criteres, la progression et le debriefing.
// Rappel AI Act, article 5 : simuler les emotions des agents est permis ; inferer les
// emotions reelles de l'apprenant est interdit en education.
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aminj.Amphi
{
    public class QuestionDuPublic : MonoBehaviour
    {
        public AgentRelais agent;
        public TraceXapi trace;
        [Tooltip("Bulle au-dessus du public.")]
        public TextMeshPro bulle;

        [TextArea(3, 8)]
        [Tooltip("En attendant la transcription de l'oral, un resume saisi a la main.")]
        public string resumeDemo = "L'orateur presente un serious game en realite virtuelle pour former a la securite incendie. " +
                                   "Il explique le scenario en quatre etapes et annonce une experimentation avec 48 participants.";

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame) DemanderUneQuestion(resumeDemo);
        }

        public void DemanderUneQuestion(string resumeExpose)
        {
            if (!agent) return;
            if (bulle) bulle.text = "…";
            agent.Demander("Voici le resume d'un expose. Pose UNE question courte, comme un auditeur de l'amphitheatre.",
                resumeExpose, question =>
                {
                    if (bulle) bulle.text = question;
                    if (trace) trace.Tracer("a_recu_une_question", "public", question);
                });
        }
    }
}
