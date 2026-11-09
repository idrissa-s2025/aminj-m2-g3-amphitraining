// Pilotage au clavier et à la souris, quand aucun casque n'est branché.
//
// Sert à vérifier la scène dans l'éditeur sans mettre le casque : mêmes
// règles de déplacement (on ne se pose que sur la zone navigable), mais visée
// au curseur. Le composant se désactive tout seul dès qu'un casque est suivi,
// pour ne jamais se battre avec les manettes.
//
//   ZQSD / WASD : glisser au sol        clic gauche : se téléporter
//   souris      : regarder              A / E : tourner de 45 degrés

using UnityEngine;
using UnityEngine.InputSystem;

namespace Amphi
{
    public class AmphiClavier : MonoBehaviour
    {
        public AmphiRig rig;
        public AmphiLocomotion locomotion;
        public float vitesse = 2.4f;
        public float sensibilite = 0.12f;
        public float hauteurYeux = 1.68f;

        float _lacet, _tangage;
        bool _actif;

        void Update()
        {
            if (rig == null || rig.head == null) return;
            _actif = !rig.CasquePresent;
            if (!_actif) return;

            var clavier = Keyboard.current;
            var souris = Mouse.current;
            if (clavier == null) return;

            var t = rig.head.transform;
            t.localPosition = new Vector3(0f, hauteurYeux, 0f);

            if (souris != null && souris.rightButton.isPressed)
            {
                Vector2 d = souris.delta.ReadValue() * sensibilite;
                _lacet += d.x;
                _tangage = Mathf.Clamp(_tangage - d.y, -85f, 85f);
                t.localRotation = Quaternion.Euler(_tangage, 0f, 0f);
                rig.transform.rotation = Quaternion.Euler(0f, _lacet, 0f);
            }

            Vector3 avance = Vector3.zero;
            if (clavier.wKey.isPressed || clavier.zKey.isPressed) avance += Vector3.forward;
            if (clavier.sKey.isPressed) avance += Vector3.back;
            if (clavier.aKey.isPressed || clavier.qKey.isPressed) avance += Vector3.left;
            if (clavier.dKey.isPressed) avance += Vector3.right;
            if (avance != Vector3.zero)
                rig.transform.position += rig.transform.TransformDirection(
                    avance.normalized) * vitesse * Time.deltaTime;

            if (clavier.eKey.wasPressedThisFrame) rig.transform.Rotate(0f, 45f, 0f);
            if (clavier.aKey.wasPressedThisFrame && clavier.leftShiftKey.isPressed)
                rig.transform.Rotate(0f, -45f, 0f);

            // un clic sur l'écran de présentation est un clic d'interface,
            // pas une téléportation
            if (souris != null && souris.leftButton.wasPressedThisFrame
                && !AmphiPointeurEcran.SourisSurEcran
                && locomotion != null && Camera.main != null)
            {
                var ray = Camera.main.ScreenPointToRay(souris.position.ReadValue());
                if (Physics.Raycast(ray, out RaycastHit hit, 60f,
                                    locomotion.calquesNavigables,
                                    QueryTriggerInteraction.Collide))
                    locomotion.Deplacer(hit.point, null);
            }
        }
    }
}
