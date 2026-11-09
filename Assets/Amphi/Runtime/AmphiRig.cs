// Suivi des périphériques XR et repère du joueur.
//
// POURQUOI PAS XR INTERACTION TOOLKIT ICI. Le déplacement demandé tient en
// deux gestes — téléportation au joystick, rotation au joystick — et le XRI
// a changé trois fois de noms d'API entre ses versions 2 et 3. Cette classe
// n'utilise que le système d'entrées et le sous-système XR d'Unity, qui sont
// stables : elle compile sans dépendre d'une version de paquet. Le XRI reste
// installable par-dessus pour de l'interaction d'objets.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Amphi
{
    [DefaultExecutionOrder(-100)]
    public class AmphiRig : MonoBehaviour
    {
        [Header("Hiérarchie")]
        public Transform cameraOffset;      // enfant du rig, porte la caméra
        public Camera head;
        public Transform leftHand;
        public Transform rightHand;

        [Header("Repère de suivi")]
        [Tooltip("Sol = la hauteur des yeux vient du casque. Décochez si votre "
               + "Quest est configuré en mode assis et que la vue est trop basse.")]
        public bool origineAuSol = true;

        [Tooltip("Hauteur des yeux appliquée si le casque ne fournit pas de "
               + "repère au sol (mode assis, ou test sans casque).")]
        public float hauteurYeuxDefaut = 1.68f;

        InputAction _hmdPos, _hmdRot, _lPos, _lRot, _rPos, _rRot, _tracked;
        bool _originSet;

        public bool CasquePresent { get; private set; }

        void Awake()
        {
            _hmdPos = new InputAction("hmdPos", InputActionType.Value,
                                      "<XRHMD>/centerEyePosition");
            _hmdRot = new InputAction("hmdRot", InputActionType.Value,
                                      "<XRHMD>/centerEyeRotation");
            _lPos = new InputAction("lPos", InputActionType.Value,
                                    "<XRController>{LeftHand}/devicePosition");
            _lRot = new InputAction("lRot", InputActionType.Value,
                                    "<XRController>{LeftHand}/deviceRotation");
            _rPos = new InputAction("rPos", InputActionType.Value,
                                    "<XRController>{RightHand}/devicePosition");
            _rRot = new InputAction("rRot", InputActionType.Value,
                                    "<XRController>{RightHand}/deviceRotation");
            _tracked = new InputAction("tracked", InputActionType.Value,
                                       "<XRHMD>/isTracked");
        }

        void OnEnable()
        {
            foreach (var a in Actions()) a.Enable();
            Application.onBeforeRender += Suivre;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= Suivre;
            foreach (var a in Actions()) a.Disable();
        }

        IEnumerable<InputAction> Actions()
        {
            yield return _hmdPos; yield return _hmdRot;
            yield return _lPos; yield return _lRot;
            yield return _rPos; yield return _rRot;
            yield return _tracked;
        }

        void Update()
        {
            if (!_originSet) DefinirOrigine();
            Suivre();
        }

        /// <summary>
        /// Demande au casque un repère de suivi AU SOL. Sans cela, l'origine
        /// est la position de la tête au démarrage : la salle se retrouve à
        /// hauteur de nombril, ou le joueur enterré jusqu'aux épaules selon la
        /// façon dont le casque a été posé.
        /// </summary>
        void DefinirOrigine()
        {
            var subs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(subs);
            if (subs.Count == 0) return;
            foreach (var s in subs)
            {
                s.TrySetTrackingOriginMode(origineAuSol
                    ? TrackingOriginModeFlags.Floor
                    : TrackingOriginModeFlags.Device);
                s.TryRecenter();
            }
            _originSet = true;
        }

        void Suivre()
        {
            CasquePresent = _tracked != null && _tracked.ReadValue<float>() > 0.5f;

            if (head != null && cameraOffset != null)
            {
                Vector3 p = _hmdPos.ReadValue<Vector3>();
                if (CasquePresent || p.sqrMagnitude > 1e-6f)
                {
                    if (!origineAuSol) p.y += hauteurYeuxDefaut;
                    head.transform.localPosition = p;
                    head.transform.localRotation = _hmdRot.ReadValue<Quaternion>();
                }
            }
            Poser(leftHand, _lPos, _lRot);
            Poser(rightHand, _rPos, _rRot);
        }

        static void Poser(Transform t, InputAction pos, InputAction rot)
        {
            if (t == null) return;
            var p = pos.ReadValue<Vector3>();
            var q = rot.ReadValue<Quaternion>();
            if (q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f) q = Quaternion.identity;
            t.localPosition = p;
            t.localRotation = q;
        }

        /// <summary>Projection au sol de la tête, dans le repère du rig.</summary>
        public Vector3 PiedsMonde()
        {
            if (head == null) return transform.position;
            var p = head.transform.position;
            return new Vector3(p.x, transform.position.y, p.z);
        }
    }
}
