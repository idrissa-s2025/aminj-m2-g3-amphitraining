// Déplacement : téléportation et rotation au joystick.
//
// Les deux joysticks font les deux choses, comme demandé :
//   - joystick POUSSE EN AVANT  -> vise la téléportation (arc + réticule),
//     relâcher = se téléporter là où pointe le réticule ;
//   - joystick POUSSE DE COTE   -> rotation par crans (45 deg par défaut),
//     ou rotation continue si `rotationContinue` est coché.
//
// La téléportation ne se pose que sur la ZONE NAVIGABLE : le maillage plat
// exporté depuis Blender (plateau, passage de chaque rang, marches des deux
// escaliers, palier haut, estrade). Impossible, donc, de finir dans un mur,
// sur une tablette ou en dehors de la salle.

using UnityEngine;
using UnityEngine.InputSystem;

namespace Amphi
{
    public class AmphiLocomotion : MonoBehaviour
    {
        public enum Main { Gauche, Droite, LesDeux }

        [Header("Références")]
        public AmphiRig rig;
        public LineRenderer arcGauche;
        public LineRenderer arcDroit;
        public Transform reticule;
        public AmphiFondu fondu;

        [Header("Commandes")]
        public Main mainTeleportation = Main.LesDeux;
        public Main mainRotation = Main.LesDeux;
        [Range(0.3f, 0.95f)] public float seuilAvant = 0.55f;
        [Range(0.3f, 0.95f)] public float seuilCote = 0.70f;
        public bool rotationContinue = false;
        public float angleCran = 45f;
        public float vitesseRotationContinue = 120f;

        [Header("Arc de visée")]
        public LayerMask calquesNavigables = ~0;
        // 9 -> 10,5 m/s le 2026-09-24 : depuis le bas de la salle, l'arc
        // n'atteignait les rangs du haut (3,4 m plus haut, 10 m plus loin)
        // qu'en visant presque à la verticale.
        public float vitesseLancer = 10.5f;
        public float gravite = -9.81f;
        public int pasArc = 60;
        public float dureeArc = 2.0f;
        [Tooltip("Pente maximale acceptée sous le réticule, en degrés.")]
        public float penteMax = 40f;

        [Header("Confort")]
        public bool fonduAuSaut = true;
        public float dureeFondu = 0.09f;

        InputAction _stickL, _stickR, _sit, _recentre;
        bool _armeRotation = true;
        bool _viseGauche, _viseDroite;
        Vector3 _cible;
        bool _cibleValide;
        AmphiSiege _siegeVise;
        AmphiSiege _siegeOccupe;

        void Awake()
        {
            _stickL = new InputAction("stickL", InputActionType.Value,
                                      "<XRController>{LeftHand}/thumbstick");
            _stickR = new InputAction("stickR", InputActionType.Value,
                                      "<XRController>{RightHand}/thumbstick");
            // s'asseoir / se lever : bouton A ou X
            _sit = new InputAction("assis", InputActionType.Button);
            _sit.AddBinding("<XRController>{RightHand}/primaryButton");
            _sit.AddBinding("<XRController>{LeftHand}/primaryButton");
            // recentrer sur l'entrée : bouton B ou Y
            _recentre = new InputAction("recentrer", InputActionType.Button);
            _recentre.AddBinding("<XRController>{RightHand}/secondaryButton");
            _recentre.AddBinding("<XRController>{LeftHand}/secondaryButton");
        }

        void OnEnable()
        {
            _stickL.Enable(); _stickR.Enable(); _sit.Enable(); _recentre.Enable();
        }

        void OnDisable()
        {
            _stickL.Disable(); _stickR.Disable(); _sit.Disable(); _recentre.Disable();
        }

        void Update()
        {
            if (rig == null) return;
            Vector2 l = _stickL.ReadValue<Vector2>();
            Vector2 r = _stickR.ReadValue<Vector2>();
            // Qui tient le panneau du présentateur l'éloigne ou le rapproche
            // au joystick : ni rotation, ni téléportation pendant ce temps.
            if (AmphiPanneauPresentateur.Saisi)
            {
                l = r = Vector2.zero;
                _cibleValide = false;
            }

            Rotation(l, r);
            Visee(l, r);

            if (_sit.WasPressedThisFrame()) BasculerAssis();
            if (_recentre.WasPressedThisFrame()) Recentrer();
        }

        // ------------------------------------------------------------ rotation
        void Rotation(Vector2 l, Vector2 r)
        {
            // Qui vise l'écran d'un document tourne les PAGES au joystick, pas
            // la salle : voir AmphiPointeurEcran.
            if (AmphiPointeurEcran.CaptureJoystick) return;
            float x = 0f;
            if (mainRotation != Main.Droite && Mathf.Abs(l.x) > Mathf.Abs(x)) x = l.x;
            if (mainRotation != Main.Gauche && Mathf.Abs(r.x) > Mathf.Abs(x)) x = r.x;
            // un joystick poussé en avant ne doit pas faire tourner : la
            // composante latérale d'un geste vers l'avant atteint vite 0,4.
            if (Mathf.Abs(x) < 0.001f) return;

            if (rotationContinue)
            {
                if (Mathf.Abs(x) > 0.2f)
                    Tourner(x * vitesseRotationContinue * Time.deltaTime);
                return;
            }
            if (Mathf.Abs(x) < 0.35f) _armeRotation = true;
            if (_armeRotation && Mathf.Abs(x) > seuilCote)
            {
                Tourner(Mathf.Sign(x) * angleCran);
                _armeRotation = false;
            }
        }

        /// <summary>
        /// Tourne AUTOUR DE LA TETE, et non autour de l'origine du rig : sans
        /// cela le joueur est emporté sur un cercle dès qu'il s'est déplacé
        /// physiquement dans sa pièce, ce qui donne le mal de cœur.
        /// </summary>
        void Tourner(float degres)
        {
            Vector3 pivot = rig.head != null ? rig.head.transform.position
                                             : rig.transform.position;
            rig.transform.RotateAround(pivot, Vector3.up, degres);
        }

        // ------------------------------------------------------------- visée
        void Visee(Vector2 l, Vector2 r)
        {
            bool gauche = mainTeleportation != Main.Droite && l.y > seuilAvant;
            bool droite = mainTeleportation != Main.Gauche && r.y > seuilAvant;

            bool relacheGauche = _viseGauche && !gauche;
            bool relacheDroite = _viseDroite && !droite;
            _viseGauche = gauche;
            _viseDroite = droite;

            Transform main = null;
            if (gauche && rig.leftHand != null) main = rig.leftHand;
            else if (droite && rig.rightHand != null) main = rig.rightHand;

            if (main != null)
            {
                Tracer(main, gauche ? arcGauche : arcDroit);
                Cacher(gauche ? arcDroit : arcGauche);
            }
            else
            {
                Cacher(arcGauche); Cacher(arcDroit);
                if (reticule != null) reticule.gameObject.SetActive(false);
            }

            if ((relacheGauche || relacheDroite) && _cibleValide) Sauter();
        }

        static void Cacher(LineRenderer lr)
        {
            if (lr != null && lr.enabled) lr.enabled = false;
        }

        void Tracer(Transform main, LineRenderer lr)
        {
            _cibleValide = false;
            _siegeVise = null;
            Vector3 p = main.position;
            Vector3 v = main.forward * vitesseLancer;
            float dt = dureeArc / pasArc;

            var pts = new Vector3[pasArc + 1];
            int n = 0;
            pts[n++] = p;
            for (int i = 0; i < pasArc; i++)
            {
                Vector3 suivant = p + v * dt + 0.5f * new Vector3(0, gravite, 0) * dt * dt;
                v += new Vector3(0, gravite, 0) * dt;
                if (Physics.Linecast(p, suivant, out RaycastHit hit,
                                     calquesNavigables, QueryTriggerInteraction.Collide))
                {
                    pts[n++] = hit.point;
                    // UN SIEGE SE PREND PAR N'IMPORTE QUELLE FACE.
                    //
                    // On exigeait une face presque horizontale sous le
                    // réticule, siège compris. Or vu du bas de la salle, l'arc
                    // arrive sur la face AVANT du volume de visée d'un siège,
                    // qui est verticale : il était refusé, et il fallait viser
                    // jusqu'à retomber pile sur le dessus. Un siège n'est pas
                    // un sol : on s'y assoit d'où qu'on le vise.
                    var siege = hit.collider.GetComponentInParent<AmphiSiege>();
                    if (siege != null)
                    {
                        _cible = hit.point;
                        _cibleValide = true;
                        _siegeVise = siege;
                    }
                    else if (Poser(hit, out Vector3 sol))
                    {
                        _cible = sol;
                        _cibleValide = true;
                    }
                    break;
                }
                p = suivant;
                pts[n++] = p;
            }

            if (lr != null)
            {
                lr.enabled = true;
                lr.positionCount = n;
                lr.SetPositions(Trim(pts, n));
                lr.startColor = lr.endColor = _cibleValide
                    ? new Color(0.35f, 0.85f, 1f, 0.9f)
                    : new Color(1f, 0.35f, 0.3f, 0.6f);
            }
            if (reticule != null)
            {
                reticule.gameObject.SetActive(_cibleValide);
                if (_cibleValide)
                {
                    // sur un siège, le réticule va AU siège : on voit où l'on
                    // s'assiéra, pas l'endroit du volume que l'arc a touché
                    var ou = _siegeVise != null ? _siegeVise.transform.position
                             + Vector3.up * (_siegeVise.hauteurAssise + 0.02f) : _cible;
                    reticule.position = ou + Vector3.up * 0.012f;
                    reticule.localScale = Vector3.one * (_siegeVise != null ? 0.34f : 0.5f);
                }
            }
        }

        /// <summary>
        /// Le point où l'on se posera pour un impact sur la zone navigable.
        /// </summary>
        /// <remarks>
        /// LES CONTREMARCHES NE SONT PLUS UN MUR. Sur un escalier, l'arc touche
        /// aussi souvent la face verticale d'une marche que son dessus, et un
        /// impact vertical était refusé : d'où les visées à répétition. On
        /// cherche alors le dessus de marche le plus proche : d'abord celle
        /// juste derrière la contremarche (on monte), puis celle juste devant
        /// (on descend), à moins de 60 cm.
        /// </remarks>
        bool Poser(RaycastHit hit, out Vector3 sol)
        {
            sol = hit.point;
            if (Vector3.Angle(hit.normal, Vector3.up) <= penteMax) return true;
            var dehors = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
            var essais = new[]
            {
                hit.point - dehors * 0.15f + Vector3.up * 0.45f,   // la marche au-dessus
                hit.point + dehors * 0.15f + Vector3.up * 0.05f,   // la marche au-dessous
            };
            foreach (var depart in essais)
            {
                if (Physics.Raycast(depart, Vector3.down, out RaycastHit h, 0.6f,
                                    calquesNavigables, QueryTriggerInteraction.Ignore)
                    && Vector3.Angle(h.normal, Vector3.up) <= penteMax)
                {
                    sol = h.point;
                    return true;
                }
            }
            return false;
        }

        static Vector3[] Trim(Vector3[] src, int n)
        {
            var dst = new Vector3[n];
            System.Array.Copy(src, dst, n);
            return dst;
        }

        // -------------------------------------------------------- déplacement
        void Sauter()
        {
            if (_siegeVise != null) { Asseoir(_siegeVise); return; }
            // on quitte le siège AVANT de bouger, sinon la surélévation
            // « assis » s'appliquerait encore au point d'arrivée
            _siegeOccupe = null;
            Deplacer(_cible, null);
        }

        void Asseoir(AmphiSiege siege)
        {
            _siegeOccupe = siege;
            Deplacer(siege.transform.position, siege.transform.rotation);
        }

        void BasculerAssis()
        {
            if (_siegeOccupe == null) return;
            _siegeOccupe.assisVirtuel = !_siegeOccupe.assisVirtuel;
            Deplacer(_siegeOccupe.transform.position, _siegeOccupe.transform.rotation);
        }

        void Recentrer()
        {
            var depart = GameObject.Find("DEPART_Entree");
            if (depart != null) Deplacer(depart.transform.position, depart.transform.rotation);
        }

        /// <summary>
        /// Hauteur des yeux du joueur au-dessus du sol de SA pièce, c'est-à-dire
        /// de l'origine du rig. Sans casque suivi, la tête est à l'origine et
        /// on prend la hauteur par défaut du rig.
        /// </summary>
        float YeuxJoueur()
        {
            if (rig.head == null) return rig.hauteurYeuxDefaut;
            float h = rig.head.transform.position.y - rig.transform.position.y;
            return h > 0.5f ? h : rig.hauteurYeuxDefaut;
        }

        /// <summary>
        /// Pose le rig de sorte que la PROJECTION AU SOL DE LA TETE tombe sur
        /// le point visé. Déplacer l'origine du rig sur ce point mettrait le
        /// joueur à côté, du décalage qu'il a pris en marchant dans sa pièce.
        /// </summary>
        public void Deplacer(Vector3 point, Quaternion? cap)
        {
            if (fondu != null && fonduAuSaut) fondu.Clignement(dureeFondu);

            float montee = 0f;
            if (_siegeOccupe != null)
                montee = _siegeOccupe.Correction(YeuxJoueur());

            Vector3 pieds = rig.PiedsMonde();
            rig.transform.position += (point + Vector3.up * montee) - pieds;

            if (cap.HasValue && rig.head != null)
            {
                float voulu = cap.Value.eulerAngles.y;
                float actuel = rig.head.transform.eulerAngles.y;
                Tourner(Mathf.DeltaAngle(actuel, voulu));
            }
        }
    }
}
