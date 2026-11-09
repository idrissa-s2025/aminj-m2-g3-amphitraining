// Le panneau du présentateur : le « PC » de la séance.
//
// Demande d'Idrissa du 2026-09-29. Le grand écran de la salle est pour les
// étudiants : il ne montre que la page projetée (AmphiProjection). Tout ce
// qui sert à conduire la séance, bibliothèque, chargement, pages, miniatures,
// vit ici, sur un panneau incurvé et translucide qui suit le présentateur,
// comme les fenêtres du Quest. Quand il tourne la page ici, elle tourne
// là-bas : un professeur devant son ordinateur, qui projette.
//
// LA FORME. Un arc de cylindre vertical dont l'axe passe par les yeux au
// moment où on le pose : tout point du panneau est à la même distance de
// l'oeil, les bords ne fuient pas, le texte garde la même taille d'un bout à
// l'autre. 1,15 m de large sur un rayon de 0,90 m, soit 73° de champ, et la
// hauteur que donne le rapport 2,46 de la toile, 0,47 m.
//
// LA NETTETE. La toile de 2460 px, rendue x 1,5, s'étale sur 73° : environ
// 50 px de texture par degré, pour un casque qui en affiche 25 au centre.
// Chaque pixel de l'écran du Quest lit donc deux texels, filtrés en
// trilinéaire : ni crénelage ni scintillement, même en bougeant la tête.
//
// LES GESTES.
//   Grip, le rayon sur le panneau ou la main près de la barre : on le saisit.
//     Il suit la main comme au bout d'une perche, reste droit et tourné vers
//     les yeux. Joystick avant / arrière pendant la saisie : on l'éloigne ou
//     on le rapproche.
//
// IL GARDE SA TAILLE A L'OEIL. Demande d'Idrissa du 2026-09-29 : éloigné, le
// panneau grandit d'autant, rapproché il rétrécit, comme les fenêtres du
// Quest. Son échelle suit le rapport distance / rayon : l'angle qu'il occupe
// dans le champ ne change pas, le texte reste aussi lisible à 3 m qu'à 90 cm,
// et l'axe du cylindre continue de passer par les yeux.
//   Bouton Menu (main gauche) : le panneau vient devant soi ; s'il y est
//     déjà, il se range. Un second appui le rappelle.
//
// IL SUIT. Le panneau est un enfant du rig : téléportation et rotation
// l'emportent avec le joueur. Et s'il s'assoit ou se lève, le panneau monte
// ou descend d'autant.

using UnityEngine;
using UnityEngine.InputSystem;

namespace Amphi
{
    public class AmphiPanneauPresentateur : MonoBehaviour
    {
        public AmphiEcranPresentation ecran;
        public AmphiRig rig;

        [Header("Matières, posées par le constructeur")]
        [Tooltip("Amphi/PanneauVerre : l'image de l'interface, en alpha prémultiplié.")]
        public Material matiereVerre;
        [Tooltip("Opaque non éclairée, pour la barre de saisie.")]
        public Material matiereBarre;

        [Header("Forme")]
        public float largeur = 1.15f;
        [Tooltip("Rayon de courbure. Égal à la distance de pose : l'axe passe par les yeux.")]
        public float rayon = 0.90f;
        [Tooltip("Sous la ligne des yeux, là où l'on regarde un écran d'ordinateur.")]
        public float sousLesYeux = 0.14f;

        [Header("Saisie")]
        public float distanceMin = 0.40f;
        public float distanceMax = 3.50f;
        [Tooltip("Bornes de l'échelle : 0,45 à 40 cm, 3,9 à 3,5 m.")]
        public float echelleMin = 0.45f, echelleMax = 3.9f;
        [Tooltip("Vitesse d'éloignement au joystick, en m/s.")]
        public float vitessePousse = 1.2f;
        [Tooltip("Raideur du suivi : plus haut, plus sec.")]
        public float raideur = 16f;

        /// <summary>Vrai pendant qu'une main tient le panneau : la
        /// locomotion laisse alors le joystick à la distance.</summary>
        public static bool Saisi { get; private set; }

        public float Hauteur => largeur * AmphiEcranPresentation.H / AmphiEcranPresentation.W;
        float Arc => largeur / rayon;                     // en radians
        public bool Visible => _opacite > 0.01f;

        MeshRenderer _verre;
        Material _matVerre, _matBarre, _matPoint;
        Transform _barre, _point;
        float _opacite, _opaciteCible;

        InputAction _gripG, _gripD, _stickG, _stickD, _menu;
        Transform _main;                // la main qui tient, null sinon
        Vector3 _prise;                 // le centre du panneau, dans le repère de la main
        bool _pose;
        float _teteRef;                 // hauteur de la tête, repère du rig, à la dernière pose
        Vector3 _posLocale;             // pose voulue, repère du rig
        Quaternion _rotLocale;
        float _echelle = 1f;
        bool _barreVisee;

        // ------------------------------------------------------------ cycle
        void Awake()
        {
            _gripG = new InputAction("gripG", InputActionType.Button, "<XRController>{LeftHand}/gripPressed");
            _gripD = new InputAction("gripD", InputActionType.Button, "<XRController>{RightHand}/gripPressed");
            _stickG = new InputAction("stickG", InputActionType.Value, "<XRController>{LeftHand}/thumbstick");
            _stickD = new InputAction("stickD", InputActionType.Value, "<XRController>{RightHand}/thumbstick");
            _menu = new InputAction("menu", InputActionType.Button);
            _menu.AddBinding("<XRController>{LeftHand}/menu");
            _menu.AddBinding("<XRController>{LeftHand}/menuButton");
            // au poste, sans casque : Tab fait la même chose
            _menu.AddBinding("<Keyboard>/tab");
        }

        void OnEnable()
        {
            _gripG.Enable(); _gripD.Enable(); _stickG.Enable(); _stickD.Enable(); _menu.Enable();
        }

        void OnDisable()
        {
            _gripG.Disable(); _gripD.Disable(); _stickG.Disable(); _stickD.Disable(); _menu.Disable();
            Saisi = false;
        }

        void Start()
        {
            if (rig == null) rig = FindFirstObjectByType<AmphiRig>();
            Construire();
            if (rig != null) transform.SetParent(rig.transform, true);
        }

        /// <summary>L'écran y pose la texture de son interface.</summary>
        public void Afficher(Texture t)
        {
            if (_matVerre == null) Construire();
            _matVerre.SetTexture("_BaseMap", t);
            _matVerre.mainTexture = t;
        }

        /// <summary>Visible tout de suite, sans fondu : pour l'aperçu.</summary>
        public void Montrer()
        {
            Construire();
            _opacite = _opaciteCible = 1f;
            Appliquer();
        }

        // ---------------------------------------------------------- maillage
        bool _construit;

        void Construire()
        {
            if (_construit) return;
            _construit = true;

            var go = new GameObject("VERRE");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = FaireArc(64);
            _verre = go.AddComponent<MeshRenderer>();
            _matVerre = matiereVerre != null ? new Material(matiereVerre)
                                             : new Material(Shader.Find("Amphi/PanneauVerre"));
            _verre.sharedMaterial = _matVerre;
            Sobre(_verre);

            // la barre de saisie, sous le panneau, comme sur le Quest
            var b = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            b.name = "BARRE_SAISIE";
            Detruire(b.GetComponent<Collider>());
            b.transform.SetParent(transform, false);
            b.transform.localPosition = new Vector3(0f, -Hauteur / 2f - 0.032f, -0.004f);
            b.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            b.transform.localScale = new Vector3(0.011f, 0.075f, 0.011f);
            _matBarre = matiereBarre != null ? new Material(matiereBarre)
                                             : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var mb = b.GetComponent<MeshRenderer>();
            mb.sharedMaterial = _matBarre;
            Sobre(mb);
            _barre = b.transform;

            // le point du rayon, un disque doux posé devant le verre
            var p = new GameObject("POINT_PANNEAU");
            p.transform.SetParent(transform, false);
            p.AddComponent<MeshFilter>().sharedMesh = Carre();
            var mp = p.AddComponent<MeshRenderer>();
            _matPoint = new Material(_matVerre) { renderQueue = 3001 };
            _matPoint.SetTexture("_BaseMap", Disque);
            mp.sharedMaterial = _matPoint;
            Sobre(mp);
            p.SetActive(false);
            _point = p.transform;

            _opacite = 0f;
            Appliquer();
        }

        /// <summary>
        /// L'arc, en repère local : l'axe du cylindre est vertical, en
        /// (0, y, -rayon), du côté du joueur ; le milieu du panneau est à
        /// l'origine ; +z s'éloigne du joueur, comme la face avant d'un Quad.
        /// </summary>
        Mesh FaireArc(int colonnes)
        {
            float a = Arc, h = Hauteur;
            var v = new Vector3[(colonnes + 1) * 2];
            var uv = new Vector2[v.Length];
            var n = new Vector3[v.Length];
            for (int i = 0; i <= colonnes; i++)
            {
                float u = i / (float)colonnes;
                float t = (u - 0.5f) * a;
                var p = new Vector3(rayon * Mathf.Sin(t), 0f, rayon * Mathf.Cos(t) - rayon);
                var dedans = -new Vector3(Mathf.Sin(t), 0f, Mathf.Cos(t));
                v[2 * i] = p + Vector3.down * h / 2f; uv[2 * i] = new Vector2(u, 0f);
                v[2 * i + 1] = p + Vector3.up * h / 2f; uv[2 * i + 1] = new Vector2(u, 1f);
                n[2 * i] = n[2 * i + 1] = dedans;
            }
            var tri = new int[colonnes * 6];
            for (int i = 0; i < colonnes; i++)
            {
                int b0 = 2 * i, h0 = b0 + 1, b1 = b0 + 2, h1 = b0 + 3;
                tri[6 * i + 0] = b0; tri[6 * i + 1] = h0; tri[6 * i + 2] = h1;
                tri[6 * i + 3] = b0; tri[6 * i + 4] = h1; tri[6 * i + 5] = b1;
            }
            var m = new Mesh { name = "PanneauArc", vertices = v, uv = uv, normals = n, triangles = tri };
            m.RecalculateBounds();
            return m;
        }

        static Mesh _carre;
        static Mesh Carre()
        {
            if (_carre != null) return _carre;
            _carre = new Mesh
            {
                name = "Point",
                vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(-.5f, .5f), new Vector3(.5f, .5f), new Vector3(.5f, -.5f) },
                uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            return _carre;
        }

        static Texture2D _disque;

        /// <summary>Un disque blanc à bord adouci, en alpha prémultiplié :
        /// le point du rayon, ici et sur le grand écran.</summary>
        public static Texture2D Disque
        {
            get
            {
                if (_disque != null) return _disque;
                const int N = 64;
                _disque = new Texture2D(N, N, TextureFormat.RGBA32, true) { name = "Disque", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[N * N];
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float d = new Vector2(x + 0.5f - N / 2f, y + 0.5f - N / 2f).magnitude;
                        // anneau sombre d'1/8 du rayon : le point se lit sur une page blanche
                        float a = Mathf.Clamp01(N / 2f - d);
                        float coeur = Mathf.Clamp01(N / 2f - 5f - d);
                        byte c = (byte)(255 * coeur), al = (byte)(255 * a);
                        px[y * N + x] = new Color32(c, c, c, al);
                    }
                _disque.SetPixels32(px);
                _disque.Apply(true, true);
                return _disque;
            }
        }

        static void Sobre(Renderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        static void Detruire(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // ------------------------------------------------------------- visée
        /// <summary>
        /// Le point visé, en coordonnées de texture (0,0 en bas à gauche), ou
        /// false. Seule compte la face concave, celle que regarde le joueur.
        /// </summary>
        public bool Viser(Ray ray, float portee, out Vector2 uv, out Vector3 impact)
        {
            uv = default; impact = default;
            if (!Visible || !_construit) return false;
            // InverseTransformVector et non Direction : le panneau peut être mis
            // à l'échelle, et t doit rester le même paramètre en local et en
            // monde pour que la portée se mesure juste.
            var o = transform.InverseTransformPoint(ray.origin);
            var d = transform.InverseTransformVector(ray.direction);
            // (ox + t dx)² + (oz + R + t dz)² = R²
            float R = rayon, oz = o.z + R;
            float a = d.x * d.x + d.z * d.z;
            if (a < 1e-8f) return false;
            float b = 2f * (o.x * d.x + oz * d.z);
            float c = o.x * o.x + oz * oz - R * R;
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return false;
            float s = Mathf.Sqrt(disc);
            float t1 = (-b - s) / (2f * a), t2 = (-b + s) / (2f * a);
            foreach (var t in new[] { t1, t2 })
            {
                if (t < 0f) continue;
                var p = o + d * t;
                float th = Mathf.Atan2(p.x, p.z + R);
                if (Mathf.Abs(th) > Arc / 2f || Mathf.Abs(p.y) > Hauteur / 2f) continue;
                // de l'intérieur vers l'extérieur : face concave
                if (d.x * Mathf.Sin(th) + d.z * Mathf.Cos(th) <= 0f) continue;
                impact = transform.TransformPoint(p);
                if ((impact - ray.origin).magnitude > portee) return false;
                uv = new Vector2(th / Arc + 0.5f, p.y / Hauteur + 0.5f);
                return true;
            }
            return false;
        }

        /// <summary>Le rayon passe-t-il sur la barre de saisie ?</summary>
        bool SurBarre(Ray ray, float tolerance = 0.035f)
        {
            if (!Visible || _barre == null) return false;
            var l = transform.InverseTransformPoint(ray.origin);
            var d = transform.InverseTransformVector(ray.direction);
            // plan de la barre : z constant, face au joueur
            float z = _barre.localPosition.z;
            if (Mathf.Abs(d.z) < 1e-5f) return false;
            float t = (z - l.z) / d.z;
            if (t < 0f) return false;
            var p = l + d * t;
            var c = _barre.localPosition;
            return Mathf.Abs(p.x - c.x) < 0.075f + tolerance && Mathf.Abs(p.y - c.y) < tolerance;
        }

        /// <summary>Le point du rayon, en coordonnées de texture ; null le cache.</summary>
        public void Point(Vector2? uv, bool presse)
        {
            if (_point == null) return;
            bool montrer = uv.HasValue && Visible && _main == null;
            if (_point.gameObject.activeSelf != montrer) _point.gameObject.SetActive(montrer);
            if (!montrer) return;
            float th = (uv.Value.x - 0.5f) * Arc, y = (uv.Value.y - 0.5f) * Hauteur;
            var dehors = new Vector3(Mathf.Sin(th), 0f, Mathf.Cos(th));
            _point.localPosition = new Vector3(rayon * dehors.x, y, rayon * dehors.z - rayon) - dehors * 0.003f;
            _point.localRotation = Quaternion.LookRotation(dehors, Vector3.up);
            // 30 px de toile, 22 quand la gâchette est tenue
            _point.localScale = Vector3.one * ((presse ? 22f : 30f) * Hauteur / AmphiEcranPresentation.H);
        }

        // ------------------------------------------------------------- gestes
        void Update()
        {
            if (!_construit) return;
            var tete = rig != null && rig.head != null ? rig.head.transform : Camera.main != null ? Camera.main.transform : null;
            if (tete == null) return;

            // La première pose attend que le suivi soit établi : aux toutes
            // premières images, le casque rend une tête à l'origine, et le
            // panneau serait posé par terre, au milieu de la salle.
            bool casque = rig != null && rig.CasquePresent;
            if (!_pose)
            {
                if (Time.timeSinceLevelLoad < (casque ? 0.5f : 1.5f)) return;
                PoserDevant(tete);
                _pose = true;
                _opaciteCible = 1f;
            }

            if (_menu.WasPressedThisFrame())
            {
                if (Visible && _opaciteCible > 0f && DansLeChamp(tete)) _opaciteCible = 0f;
                else { PoserDevant(tete); _opaciteCible = 1f; }
            }

            if (casque) Saisir(tete);

            if (_main == null)
            {
                // assis, debout : le panneau suit la hauteur des yeux
                float dy = HauteurTete(tete) - _teteRef;
                var voulu = _posLocale + Vector3.up * dy;
                if (transform.parent != null)
                {
                    float k = 1f - Mathf.Exp(-raideur * 0.5f * Time.deltaTime);
                    transform.localPosition = Vector3.Lerp(transform.localPosition, voulu, k);
                    transform.localRotation = Quaternion.Slerp(transform.localRotation, _rotLocale, k);
                }
            }

            _opacite = Mathf.MoveTowards(_opacite, _opaciteCible, Time.deltaTime / 0.18f);
            Appliquer();
        }

        void Saisir(Transform tete)
        {
            if (_main == null)
            {
                Transform prise = null;
                if (_gripD.WasPressedThisFrame()) prise = rig.rightHand;
                else if (_gripG.WasPressedThisFrame()) prise = rig.leftHand;
                _barreVisee = false;
                foreach (var m in new[] { rig.rightHand, rig.leftHand })
                    if (m != null && (SurBarre(new Ray(m.position, m.forward))
                                      || Vector3.Distance(m.position, _barre.position) < 0.14f))
                        _barreVisee = true;
                if (prise == null || !Visible) return;
                var r = new Ray(prise.position, prise.forward);
                bool touche = Viser(r, distanceMax + 0.5f, out _, out _) || SurBarre(r)
                              || Vector3.Distance(prise.position, _barre.position) < 0.14f;
                if (!touche) return;
                _main = prise;
                _prise = _main.InverseTransformPoint(transform.position);
                Saisi = true;
                return;
            }

            // tenu : au bout de la perche, tourné vers les yeux
            bool gauche = _main == rig.leftHand;
            bool tenu = (gauche ? _gripG : _gripD).IsPressed();
            if (!tenu)
            {
                _main = null;
                Saisi = false;
                Memoriser(tete);
                return;
            }
            float y = (gauche ? _stickG : _stickD).ReadValue<Vector2>().y;
            if (Mathf.Abs(y) > 0.2f)
            {
                // on pousse le long du rayon de la main
                var monde = _main.TransformPoint(_prise);
                var dir = monde - _main.position;
                float dist = Mathf.Clamp(dir.magnitude + y * vitessePousse * Time.deltaTime, distanceMin, distanceMax);
                _prise = _main.InverseTransformPoint(_main.position + dir.normalized * dist);
            }
            var cible = _main.TransformPoint(_prise);
            var rot = Face(cible, tete.position);
            float k = 1f - Mathf.Exp(-raideur * Time.deltaTime);
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, cible, k),
                                             Quaternion.Slerp(transform.rotation, rot, k));
            // la taille à l'oeil ne change pas : l'échelle suit la distance
            float voulue = Mathf.Clamp(Vector3.Distance(cible, tete.position) / rayon, echelleMin, echelleMax);
            _echelle = Mathf.Lerp(_echelle, voulue, k);
            transform.localScale = Vector3.one * _echelle;
        }

        /// <summary>Droit, et tourné vers les yeux : +z part des yeux vers le panneau.</summary>
        static Quaternion Face(Vector3 panneau, Vector3 yeux)
        {
            var d = panneau - yeux;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            return Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        void PoserDevant(Transform tete)
        {
            var avant = tete.forward; avant.y = 0f;
            if (avant.sqrMagnitude < 1e-4f) avant = Vector3.forward;
            avant.Normalize();
            var pos = tete.position + avant * rayon + Vector3.down * sousLesYeux;
            transform.SetPositionAndRotation(pos, Face(pos, tete.position));
            _echelle = 1f;
            transform.localScale = Vector3.one;
            Memoriser(tete);
        }

        void Memoriser(Transform tete)
        {
            _posLocale = transform.localPosition;
            _rotLocale = transform.localRotation;
            _teteRef = HauteurTete(tete);
        }

        float HauteurTete(Transform tete) =>
            transform.parent != null ? transform.parent.InverseTransformPoint(tete.position).y : tete.position.y;

        bool DansLeChamp(Transform tete)
        {
            var d = transform.position - tete.position;
            return d.magnitude < distanceMax + 0.5f && Vector3.Angle(tete.forward, d) < 40f;
        }

        void Appliquer()
        {
            bool vis = _opacite > 0.001f;
            if (_verre.enabled != vis) _verre.enabled = vis;
            if (_barre.gameObject.activeSelf != vis) _barre.gameObject.SetActive(vis);
            _matVerre.SetFloat("_Opacite", _opacite);
            _matPoint.SetFloat("_Opacite", _opacite);
            // la barre s'éclaire quand on peut la prendre, et plus encore tenue
            float e = _main != null ? 1f : _barreVisee ? 0.8f : 0.45f;
            var c = Color.Lerp(AmphiTheme.Surface3, AmphiTheme.Marbre, e);
            if (_matBarre.HasProperty("_BaseColor")) _matBarre.SetColor("_BaseColor", c);
            _matBarre.color = c;
            float l = _main != null || _barreVisee ? 0.085f : 0.075f;
            _barre.localScale = new Vector3(0.011f, l, 0.011f);
        }
    }
}
