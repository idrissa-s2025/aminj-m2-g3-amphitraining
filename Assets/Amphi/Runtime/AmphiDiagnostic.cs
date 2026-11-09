// Affichage de contrôle, dans le casque : images par seconde, triangles,
// position, siège occupé.
//
// Un casque ne montre pas la console : sans cet affichage, « ça rame » est la
// seule mesure dont on dispose. Il se déclenche par le bouton MENU de la
// manette gauche et se pose en panneau devant le joueur.
//
// Budget à tenir sur Quest 3 : 72 images par seconde, soit 13,8 ms par image.

using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Amphi
{
    public class AmphiDiagnostic : MonoBehaviour
    {
        public AmphiRig rig;
        public TextMesh texte;
        public float periode = 0.4f;
        /// <summary>Intervalle entre deux lignes de journal, en secondes.</summary>
        /// <remarks>
        /// Plus long que l'affichage : le panneau doit réagir vite sous les
        /// yeux, le journal n'a pas besoin d'une ligne toutes les 0,4 s.
        /// </remarks>
        public float periodeJournal = 2f;

        InputAction _menu;
        float _t, _acc, _journal;
        int _images;
        readonly StringBuilder _sb = new StringBuilder(256);

        void Awake()
        {
            _menu = new InputAction("menu", InputActionType.Button);
            _menu.AddBinding("<XRController>{LeftHand}/menuButton");
            _menu.AddBinding("<Keyboard>/f1");
            Afficher(false);
        }

        void OnEnable() { _menu.Enable(); }
        void OnDisable() { _menu.Disable(); }

        void Afficher(bool v)
        {
            if (texte != null) texte.gameObject.SetActive(v);
        }

        void Update()
        {
            if (_menu.WasPressedThisFrame() && texte != null)
                Afficher(!texte.gameObject.activeSelf);

            // ON MESURE TOUJOURS, MEME PANNEAU FERME.
            //
            // L'accumulation était placée APRES le test de visibilité : sans
            // le bouton Menu, rien n'était compté, et le budget image
            // n'existait que pour quelqu'un qui porte le casque et pense à
            // ouvrir le panneau. Une session qui pose l'APK par adb ne pouvait
            // donc rien mesurer, et « ça rame » restait la seule donnée.
            //
            // Le chiffre est en plus plus juste ainsi : le panneau ouvert
            // coûte lui-même quelques dixièmes de milliseconde, qu'on mesurait
            // sans le dire.
            _images++;
            _acc += Time.unscaledDeltaTime;
            _t += Time.unscaledDeltaTime;
            if (_t < periode) return;

            float ms = _acc / Mathf.Max(1, _images) * 1000f;

            // Et on le DIT, pour que `adb logcat -s Unity` le lise. Le
            // préfixe est stable et unique : il se filtre d'une commande.
            _journal += periode;
            if (_journal >= periodeJournal)
            {
                _journal = 0f;
                Debug.Log($"[AmphiPerf] {1000f / Mathf.Max(0.01f, ms):F0} im/s  "
                          + $"{ms:F2} ms  (budget 13,80 ms)  {SystemInfo.graphicsDeviceName}");
            }

            if (texte == null || !texte.gameObject.activeSelf)
            {
                _t = _acc = 0f;
                _images = 0;
                return;
            }

            _sb.Clear();
            _sb.AppendLine($"{1000f / Mathf.Max(0.01f, ms):F0} im/s   {ms:F1} ms");
            if (rig != null)
            {
                var p = rig.transform.position;
                _sb.AppendLine($"position  {p.x:F2}  {p.y:F2}  {p.z:F2}");
                if (rig.head != null)
                    _sb.AppendLine($"yeux      {rig.head.transform.position.y:F2} m");
            }
            _sb.Append(SystemInfo.graphicsDeviceName);
            texte.text = _sb.ToString();
            _t = _acc = 0f;
            _images = 0;

            // le panneau suit la tête, à hauteur d'œil et à bonne distance
            if (rig != null && rig.head != null)
            {
                var h = rig.head.transform;
                texte.transform.position = h.position + h.forward * 1.1f - h.up * 0.28f;
                texte.transform.rotation = Quaternion.LookRotation(
                    texte.transform.position - h.position, Vector3.up);
            }
        }
    }
}
