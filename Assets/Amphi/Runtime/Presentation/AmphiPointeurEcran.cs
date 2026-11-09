// Viser l'écran de présentation et cliquer dessus.
//
// DANS LE CASQUE. Chaque manette porte un rayon. Dès qu'il coupe l'écran, un
// trait apparaît de la main à l'écran, et un point à l'endroit visé. La
// GACHETTE clique : on presse sur un bouton, on relâche sur le même, le clic
// part, comme avec une souris. Et tant qu'on vise l'écran d'un document, le
// JOYSTICK poussé à gauche ou à droite tourne les pages ; la rotation du
// joueur est alors suspendue, pour ne pas tourner la salle avec la page.
//
// LE BALAYAGE. Demande d'Idrissa du 2026-09-28 : un geste de la main, comme
// sur une tablette, tourne aussi la page. On vise l'écran d'un document, on
// balaie de la main vers la gauche : page suivante ; vers la droite : page
// précédente. Le geste se juge sur la MAIN, pas sur le rayon : un balayage
// franc sort le rayon de l'écran avant la fin du geste, il suffit donc qu'il
// y soit AU DEPART. Un balayage est un déplacement latéral d'au moins 18 cm
// en moins de 0,4 s, gâchette relâchée, et nettement plus latéral que
// vertical, pour ne pas le confondre avec un bras qui se baisse. Un geste
// par balayage : on ne réarme qu'après un temps mort et un retour du rayon
// sur l'écran.
//
// LE PANNEAU ET LE GRAND ECRAN. Depuis le 2026-09-29, l'interface vit sur le
// panneau du présentateur, et le grand écran ne fait que projeter. Le rayon
// vise d'abord le panneau, tout proche ; s'il le manque, il cherche le grand
// écran. Là, pas de boutons : le point fait office de pointeur laser, la
// gâchette passe à la page suivante, joystick et balayage tournent les pages,
// comme avec une télécommande de présentation. Tant qu'une main tient le
// panneau au grip, le pointeur se tait.
//
// AU POSTE, sans casque. La souris vise, le clic gauche clique. Les flèches et
// Page préc. / Page suiv. tournent les pages, F bascule le plein écran,
// Échap revient en arrière. Au chargement, le clavier tape une adresse.

using UnityEngine;
using UnityEngine.InputSystem;

namespace Amphi
{
    public class AmphiPointeurEcran : MonoBehaviour
    {
        public AmphiEcranPresentation ecran;
        public AmphiRig rig;
        [Tooltip("Le grand écran, qui projette. Trouvé seul s'il est vide.")]
        public AmphiProjection projection;
        public float portee = 30f;
        [Tooltip("Matière du trait, celle de l'arc de téléportation.")]
        public Material matiereTrait;

        /// <summary>Vrai tant qu'un rayon vise l'écran d'un document : la
        /// locomotion laisse alors le joystick latéral au changement de page.</summary>
        public static bool CaptureJoystick { get; private set; }

        /// <summary>Vrai si la souris vise l'écran : le pilotage au clavier ne
        /// doit pas téléporter sur ce clic-là.</summary>
        public static bool SourisSurEcran { get; private set; }

        InputAction _gachetteG, _gachetteD, _stickG, _stickD;
        LineRenderer _traitG, _traitD;
        AmphiBouton _survol, _presse;
        bool _arme = true;
        bool _gauche;
        Ray? _rayon;

        // balayage : origine du geste en cours, et temps mort après un geste
        [Tooltip("Course latérale de la main qui vaut un balayage, en mètres.")]
        public float balayageCourse = 0.18f;
        [Tooltip("Durée maximale du geste, en secondes.")]
        public float balayageDuree = 0.40f;
        [Tooltip("Temps mort entre deux balayages, en secondes.")]
        public float balayageTempsMort = 0.60f;
        bool _balayageEnCours;
        Vector3 _balayageOrigine;
        float _balayageDepart, _balayageRearme;

        void Awake()
        {
            _gachetteG = new InputAction("gachetteG", InputActionType.Button,
                                         "<XRController>{LeftHand}/triggerPressed");
            _gachetteD = new InputAction("gachetteD", InputActionType.Button,
                                         "<XRController>{RightHand}/triggerPressed");
            _stickG = new InputAction("stickG", InputActionType.Value,
                                      "<XRController>{LeftHand}/thumbstick");
            _stickD = new InputAction("stickD", InputActionType.Value,
                                      "<XRController>{RightHand}/thumbstick");
        }

        void OnEnable()
        {
            _gachetteG.Enable(); _gachetteD.Enable(); _stickG.Enable(); _stickD.Enable();
            if (Keyboard.current != null) Keyboard.current.onTextInput += Taper;
        }

        void OnDisable()
        {
            _gachetteG.Disable(); _gachetteD.Disable(); _stickG.Disable(); _stickD.Disable();
            if (Keyboard.current != null) Keyboard.current.onTextInput -= Taper;
            CaptureJoystick = SourisSurEcran = false;
            if (projection != null) projection.Point(null, false);
        }

        AmphiAffiche[] _affiches;

        void Start()
        {
            if (rig == null) rig = FindFirstObjectByType<AmphiRig>();
            if (projection == null) projection = FindFirstObjectByType<AmphiProjection>();
            _affiches = FindObjectsByType<AmphiAffiche>(FindObjectsSortMode.None);
            _traitG = Trait("RayonEcranG");
            _traitD = Trait("RayonEcranD");
        }

        LineRenderer Trait(string nom)
        {
            var go = new GameObject(nom);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = 0.006f;
            // La matière de l'arc, posée par le constructeur : un shader
            // cherché par son nom n'est pas forcément dans le build.
            lr.sharedMaterial = matiereTrait != null ? matiereTrait
                                                     : new Material(Shader.Find("Sprites/Default"));
            lr.startColor = new Color(0.95f, 0.93f, 0.89f, 0.05f);
            lr.endColor = new Color(0.95f, 0.93f, 0.89f, 0.85f);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        void Update()
        {
            if (ecran == null) return;
            bool casque = rig != null && rig.CasquePresent;

            if (AmphiPanneauPresentateur.Saisi)
            {
                _traitG.enabled = _traitD.enabled = false;
                ecran.Point(null, false);
                if (projection != null) projection.Point(null, false);
                CaptureJoystick = false;
                _balayageEnCours = false;
                if (_presse != null) { _presse.Presse(false); _presse = null; ecran.Sale(); }
                return;
            }

            Vector2? px = null;
            bool presse = false, relache = false, tenu = false;
            float stickX = 0f;

            if (casque)
            {
                SourisSurEcran = false;
                // UN SEUL RAYON : CELUI DE LA MAIN ACTIVE.
                //
                // Les deux mains avaient chacune le leur, et selon l'ordre des
                // tests les deux traits pouvaient apparaître ensemble. Comme
                // dans l'interface du Quest, une seule main vise : la dernière
                // dont on a pressé la gâchette, la droite au départ. Presser la
                // gâchette de l'autre main lui passe la main, et clique.
                if (_gachetteG.WasPressedThisFrame()) _gauche = true;
                if (_gachetteD.WasPressedThisFrame()) _gauche = false;
                var main = _gauche ? rig.leftHand : rig.rightHand;
                (_gauche ? _traitD : _traitG).enabled = false;
                px = Viser(main, _gauche ? _traitG : _traitD);
                var gachette = _gauche ? _gachetteG : _gachetteD;
                presse = gachette.WasPressedThisFrame();
                relache = gachette.WasReleasedThisFrame();
                tenu = gachette.IsPressed();
                if (px.HasValue || _surProjection) stickX = (_gauche ? _stickG : _stickD).ReadValue<Vector2>().x;
                if (main != null) _rayon = new Ray(main.position, main.forward);
                Balayer(main, px.HasValue || _surProjection, tenu);
            }
            else
            {
                _balayageEnCours = false;
                _traitG.enabled = _traitD.enabled = false;
                _surProjection = false;
                var souris = Mouse.current;
                var cam = Camera.main;
                if (souris != null && cam != null)
                {
                    var r = cam.ScreenPointToRay(souris.position.ReadValue());
                    px = Toile(r);
                    ViserProjection(px.HasValue ? (Ray?)null : r);
                    presse = souris.leftButton.wasPressedThisFrame;
                    relache = souris.leftButton.wasReleasedThisFrame;
                    tenu = souris.leftButton.isPressed;
                }
                SourisSurEcran = px.HasValue || _surProjection;
                Clavier();
            }

            // le grand écran : pointeur laser et télécommande
            if (projection != null)
            {
                projection.Point(_surProjection ? _uvProjection : (Vector2?)null, tenu);
                if (_surProjection && presse) ecran.Tourner(1);
            }

            // l'affiche : viser et cliquer passe à l'image suivante
            if (!px.HasValue && !_surProjection && presse && _affiches != null)
            {
                Ray? r = casque ? _rayon
                                : (Camera.main != null && Mouse.current != null
                                   ? Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue()) : (Ray?)null);
                foreach (var a in _affiches)
                    if (a != null && r.HasValue && a.Touche(r.Value, portee)) { a.Suivante(); break; }
            }

            CaptureJoystick = (px.HasValue && ecran.VueCourante == AmphiEcranPresentation.Vue.Document)
                              || (_surProjection && ecran.Projette);
            ecran.Point(px, tenu && px.HasValue);

            // survol
            var sous = px.HasValue ? Cible(px.Value) : null;
            if (sous != _survol)
            {
                if (_survol != null) _survol.Survol(false);
                _survol = sous;
                if (_survol != null) _survol.Survol(true);
                ecran.Sale();
            }
            // clic : presser et relâcher sur le même bouton
            if (presse && sous != null) { _presse = sous; _presse.Presse(true); ecran.Sale(); }
            if (relache)
            {
                ecran.Sale();
                var p = _presse;
                if (p != null) p.Presse(false);
                _presse = null;
                if (p != null && p == sous && p.Actif) p.clic?.Invoke();
            }

            // joystick : une page par geste, retour au neutre obligatoire
            if (CaptureJoystick)
            {
                if (Mathf.Abs(stickX) < 0.35f) _arme = true;
                else if (_arme && Mathf.Abs(stickX) > 0.7f)
                {
                    ecran.Tourner(stickX > 0 ? 1 : -1);
                    _arme = false;
                }
            }
        }

        /// <summary>Balayage de la main active : vers la gauche, page suivante ;
        /// vers la droite, page précédente. Voir l'en-tête du fichier.</summary>
        void Balayer(Transform main, bool surEcran, bool gachetteTenue)
        {
            if (main == null || rig == null || rig.head == null
                || !ecran.Projette
                || gachetteTenue || Time.time < _balayageRearme)
            {
                _balayageEnCours = false;
                return;
            }
            // Le geste part du rayon posé sur l'écran ; ensuite il peut en sortir.
            if (!_balayageEnCours)
            {
                if (!surEcran) return;
                _balayageEnCours = true;
                _balayageOrigine = main.position;
                _balayageDepart = Time.time;
                return;
            }
            // Fenêtre glissante : passé le délai, le geste repart d'ici.
            if (Time.time - _balayageDepart > balayageDuree)
            {
                _balayageOrigine = main.position;
                _balayageDepart = Time.time;
                return;
            }
            // Le latéral est celui de la TETE, pas celui de l'écran : c'est de
            // là que le joueur juge sa droite et sa gauche, où qu'il se tienne
            // dans la salle.
            var droite = rig.head.transform.right;
            droite.y = 0f;
            if (droite.sqrMagnitude < 1e-4f) return;
            droite.Normalize();
            var course = main.position - _balayageOrigine;
            float lateral = Vector3.Dot(course, droite);
            float vertical = Mathf.Abs(course.y);
            if (Mathf.Abs(lateral) < balayageCourse || Mathf.Abs(lateral) < 2f * vertical) return;
            ecran.Tourner(lateral < 0f ? 1 : -1);
            _balayageEnCours = false;
            _balayageRearme = Time.time + balayageTempsMort;
        }

        bool _surProjection;
        Vector2 _uvProjection;

        Vector2? Viser(Transform main, LineRenderer trait)
        {
            if (main == null) { trait.enabled = false; _surProjection = false; return null; }
            var ray = new Ray(main.position, main.forward);
            var px = Toile(ray, out Vector3 impact);
            if (!px.HasValue) ViserProjection(ray, out impact);
            else _surProjection = false;
            trait.enabled = px.HasValue || _surProjection;
            if (trait.enabled)
            {
                trait.SetPosition(0, main.position);
                trait.SetPosition(1, impact);
            }
            return px;
        }

        void ViserProjection(Ray? ray) => ViserProjection(ray, out _);

        /// <summary>Le grand écran, quand le panneau n'est pas visé.</summary>
        void ViserProjection(Ray? ray, out Vector3 impact)
        {
            impact = default;
            _surProjection = ray.HasValue && projection != null
                             && projection.Viser(ray.Value, portee, out _uvProjection, out impact);
        }

        Vector2? Toile(Ray ray) => Toile(ray, out _);

        Vector2? Toile(Ray ray, out Vector3 impact) =>
            ecran.Viser(ray, portee, out Vector2 px, out impact) ? px : (Vector2?)null;

        /// <summary>Pour le test automatique d'interaction de l'éditeur.</summary>
        public AmphiBouton CibleSous(Vector2 px) => Cible(px);

        /// <summary>Le bouton sous le point : le dernier dessiné gagne, comme
        /// à l'écran.</summary>
        AmphiBouton Cible(Vector2 px)
        {
            var monde = ecran.PointToile(px);
            for (int i = ecran.cibles.Count - 1; i >= 0; i--)
            {
                var b = ecran.cibles[i];
                // activeInHierarchy, et NON isActiveAndEnabled : le bouton se
                // met en veille (enabled = false) dès que son animation est
                // finie, et le test précédent l'écartait. Plus aucun bouton
                // n'était visé : l'écran ne réagissait plus à rien.
                if (b == null || !b.gameObject.activeInHierarchy) continue;
                var local = b.Rect.InverseTransformPoint(monde);
                if (b.Rect.rect.Contains(new Vector2(local.x, local.y))) return b;
            }
            return null;
        }

        void Clavier()
        {
            var k = Keyboard.current;
            if (k == null) return;
            if (ecran.VueCourante == AmphiEcranPresentation.Vue.Chargement)
            {
                // saisie d'une adresse
                if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)
                    ecran.Saisir("", true, false);
                else if (k.backspaceKey.wasPressedThisFrame) ecran.Saisir("", false, true);
                else if (k.escapeKey.wasPressedThisFrame) ecran.Retour();
                return;
            }
            if (k.rightArrowKey.wasPressedThisFrame || k.pageDownKey.wasPressedThisFrame) ecran.Tourner(1);
            if (k.leftArrowKey.wasPressedThisFrame || k.pageUpKey.wasPressedThisFrame) ecran.Tourner(-1);
            if (k.fKey.wasPressedThisFrame) ecran.BasculerPleinEcran();
            if (k.escapeKey.wasPressedThisFrame) ecran.Retour();
        }

        // Les caractères tapés arrivent par l'événement texte du clavier, qui
        // respecte la disposition (AZERTY) et les majuscules.
        void Taper(char c)
        {
            if (ecran == null || char.IsControl(c)) return;
            if (ecran.VueCourante == AmphiEcranPresentation.Vue.Chargement)
                ecran.Saisir(c.ToString(), false, false);
        }
    }
}
