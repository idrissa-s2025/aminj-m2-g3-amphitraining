// L'écran de présentation : accueil, chargement, document.
//
// Il reproduit la maquette validée par Idrissa le 2026-09-23
// (Design/2026-09-23_amphitraining-vr_fig_v01.html) : une toile de
// 2460 x 1000 px posée sur la dalle LED de 7,92 x 3,22 m, soit 3,22 mm par
// pixel. Les cotes ci-dessous sont celles de la fiche de spécification.
//
// LE DOCUMENT EST « RESPONSIVE ». Il garde son propre rapport d'aspect et
// prend la plus grande place possible dans la zone document :
//
//     zone  = 1636 x 920 en (412, 40), soit la toile moins la marge M = 40
//             et, de chaque côté, une bande d'au moins 340 px et une
//             gouttière de 32 px
//     s     = min(1636 / largeur, 920 / hauteur)
//     bande = x - M - G, identique à gauche et à droite
//
// Une diapositive 16:9 remplit la zone et laisse deux bandes de 340 px ; une
// page A4 portrait laisse 833 px de chaque côté. Au-delà de 500 px, la bande
// passe en mise en page « large » : boutons côte à côte, miniatures en ligne.
// En plein écran, le document prend la toile entière.
//
// L'interface est fabriquée ici, au démarrage, et non dans la scène : comme
// le reste de la salle, elle est une SORTIE du code, reconstruite à
// l'identique à chaque lancement.

using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Amphi
{
    public class AmphiEcranPresentation : MonoBehaviour
    {
        public const float W = 2460f, H = 1000f;

        [Header("Séance, affichée à l'accueil")]
        public string surtitre = "SÉANCE EN COURS · AMPHI PRINCIPAL";
        public string titreSeance = "Formation";
        public string sousTitre = "Choisissez un document, ou chargez le vôtre.";

        [Header("Rendu en texture")]
        [Tooltip("Matière opaque non éclairée de la surface, posée par le constructeur.")]
        public Material matiereSurface;

        [Header("Panneau du présentateur")]
        [Tooltip("Le panneau incurvé qui porte cette interface. Vide : l'ancien "
               + "écran, posé sur la dalle.")]
        public AmphiPanneauPresentateur panneau;

        /// <summary>Levé à chaque changement : vue, page, page rendue. Le grand
        /// écran (AmphiProjection) s'y abonne.</summary>
        public event System.Action Change;

        // ------------------------------------------------ la salle des agents
        // Posés par AmphiSalle (s'il y a une salle) : le bouton « Configurer la
        // salle » du document, et l'état de la salle affiché sous le bouton.
        public static System.Action<AmphiDocument> Configurer;
        public static System.Func<string> EtatSalle;
        public static System.Func<bool> SalleConfigureePourDocument;
        /// <summary>Un document vient d'être ouvert (la salle s'y configure si elle le connaît déjà).</summary>
        public event System.Action<AmphiDocument> DocumentOuvert;
        public AmphiDocument DocumentCourant => _doc;

        /// <summary>Relit le dossier des présentations (après l'installation des supports livrés).</summary>
        public void RafraichirBibliotheque()
        {
            _biblio?.Actualiser();
            if (VueCourante == Vue.Accueil) Redessiner();
        }

        /// <summary>Les pages du document ouvert, rendues pour l'analyse (au plus <paramref name="max"/>).</summary>
        public System.Collections.IEnumerator PagesPourAnalyse(int largeur, int max, List<Texture2D> sortie)
        {
            if (_pdf == null) yield break;
            for (int i = 0; i < Mathf.Min(_pdf.Pages, max); i++)
                yield return _pdf.Rendre(i, largeur, t => { if (t != null) sortie.Add(t); });
        }

        public bool PagesRetournees => _pdf != null && _pdf.Retournee;

        [Header("Polices (Source Sans 3, OFL)")]
        public Font regular;
        public Font semibold;
        public Font bold;

        public enum Vue { Accueil, Chargement, Document }

        /// <summary>Un document est ouvert, donc projeté, quelle que soit la
        /// vue du panneau.</summary>
        public bool Projette => _pdf != null;
        public Vue VueCourante { get; private set; }
        public bool PleinEcran { get; private set; }

        /// <summary>Toutes les cibles cliquables visibles, pour le pointeur.</summary>
        public readonly List<AmphiBouton> cibles = new List<AmphiBouton>();

        Canvas _canvas;
        RectTransform _toile, _vAccueil, _vChargement, _vDocument, _point;
        readonly AmphiBibliotheque _biblio = new AmphiBibliotheque();

        // accueil
        string _categorie = "Cours";
        int _pageCartes;
        readonly Dictionary<string, Texture2D> _couvertures = new Dictionary<string, Texture2D>();

        // chargement
        int _pageFichiers, _pageLiens;
        string _url = "", _etat = "choix", _message;
        float _avance; long _recu;
        AmphiDocument _telecharge;
        Coroutine _tache;

        // document
        AmphiDocument _doc;
        AmphiPdf _pdf;
        int _page;
        readonly Dictionary<int, Texture2D> _cache = new Dictionary<int, Texture2D>();
        readonly HashSet<int> _enCours = new HashSet<int>();

        // ------------------------------------------------------------ cycle
        void Start()
        {
            Demarrer();
            if (Application.isPlaying) MonterTexture();
        }

        // ------------------------------------------------- rendu en texture
        //
        // NETTETE VUE DU FOND DE SALLE. Posée telle quelle dans la scène,
        // l'interface est dessinée pixel par pixel sur la dalle, SANS mipmap :
        // vue à 15 ou 20 m, un trait de lettre de 3 px tombe entre deux
        // pixels du casque, le texte scintille et se brouille, et la page du
        // document avec. L'interface est donc rendue dans une texture de
        // 2460 x 1000 à mipmaps, filtrée en trilinéaire et anisotrope, que la
        // dalle affiche. De loin, le casque lit un niveau réduit proprement
        // au lieu d'un échantillonnage au hasard.
        //
        // La caméra ne rend QUE lorsque l'écran change : une page, un survol,
        // une vignette arrivée. Au repos, l'écran ne coûte rien. Le point du
        // laser, qui bouge à chaque image, est posé devant la dalle, dans la
        // scène, pour ne pas forcer un rendu par image.

        /// <summary>La dalle qui affiche l'écran ; null dans l'aperçu de
        /// l'éditeur, où l'interface reste posée telle quelle.</summary>
        public Transform Surface { get; private set; }

        const float Surech = 1.5f;
        Camera _cam;
        RenderTexture _rt;
        bool _sale = true;
        Transform _pointMonde;
        Material _matPoint;

        /// <summary>À appeler quand l'apparence change sans Redessiner
        /// (survol d'un bouton, par exemple).</summary>
        public void Sale() => _sale = true;

        /// <summary>Rend l'interface dans sa texture. Appelé par Start, ou par
        /// l'aperçu de l'éditeur ; une seule fois.</summary>
        public void MonterTexture()
        {
            if (_rt != null) return;
            float e = transform.lossyScale.x;
            var support = transform.parent;

            // SURECHANTILLONNEE x 1,5, ET BIAIS DE MIPMAP NEGATIF.
            //
            // Rendue à 3690 x 1500 pour une toile de 2460 x 1000 : chaque
            // pixel de toile porte 2,25 échantillons, les bords de lettre sont
            // lissés avant même la réduction, et les mipmaps partent d'une
            // image plus fine. Le biais de -0,4 fait choisir au casque un
            // niveau un peu plus détaillé que la stricte théorie : le texte
            // reste net au fond de la salle, sans encore scintiller.
            _rt = new RenderTexture(Mathf.RoundToInt(W * Surech), Mathf.RoundToInt(H * Surech), 16,
                                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "EcranPresentation",
                useMipMap = true,
                autoGenerateMips = true,
                anisoLevel = 8,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                mipMapBias = -0.4f,
            };
            _rt.Create();

            // SUR LE PANNEAU DU PRESENTATEUR, la texture va au verre incurvé,
            // vidée à la transparence : le fond arrondi en fait une vitre
            // fumée, et les coins restent vides. Le grand écran, lui, est à
            // AmphiProjection.
            if (panneau != null)
            {
                panneau.Afficher(_rt);
                Eloigner(e, Color.clear);
                return;
            }

            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "SURFACE_ECRAN";
            Detruire(q.GetComponent<Collider>());
            q.transform.SetParent(support, false);
            q.transform.localPosition = transform.localPosition;
            q.transform.localRotation = transform.localRotation;
            q.transform.localScale = new Vector3(W * e, H * e, 1f);
            var mat = matiereSurface != null ? new Material(matiereSurface)
                                             : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.mainTexture = _rt;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _rt);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            Surface = q.transform;

            Eloigner(e, AmphiTheme.Ecran);

            // point du laser, devant la dalle
            var pt = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pt.name = "POINT_LASER";
            Detruire(pt.GetComponent<Collider>());
            pt.transform.SetParent(support, false);
            pt.transform.localRotation = transform.localRotation;
            pt.transform.localScale = Vector3.one * (30f * e);
            _matPoint = new Material(mat) { mainTexture = null };
            if (_matPoint.HasProperty("_BaseMap")) _matPoint.SetTexture("_BaseMap", null);
            pt.GetComponent<MeshRenderer>().sharedMaterial = _matPoint;
            pt.SetActive(false);
            _pointMonde = pt.transform;
            if (_point != null) _point.gameObject.SetActive(false);
            _sale = true;
        }

        /// <summary>L'interface part loin sous la salle, avec la caméra qui
        /// la rend dans la texture.</summary>
        void Eloigner(float e, Color fond)
        {
            // L'interface part loin sous la salle, hors de vue et hors de
            // portée des caméras du casque (plan lointain à 80 m).
            transform.SetParent(null, true);
            transform.SetPositionAndRotation(new Vector3(0f, -400f, 0f), Quaternion.identity);

            var cg = new GameObject("CAM_ECRAN");
            cg.transform.SetPositionAndRotation(transform.position - Vector3.forward * 2f, Quaternion.identity);
            _cam = cg.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = H * e / 2f;
            _cam.aspect = W / H;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 5f;
            _cam.cullingMask = 1 << gameObject.layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = fond;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.targetTexture = _rt;
            _cam.enabled = false;
            if (_point != null) _point.gameObject.SetActive(false);
            _sale = true;
        }

        /// <summary>La texture de l'interface, pour les mesures de l'aperçu.</summary>
        public RenderTexture Texture => _rt;

        /// <summary>Pour l'aperçu de l'éditeur, où Update ne tourne pas.</summary>
        public void RendreMaintenant()
        {
            Canvas.ForceUpdateCanvases();
            if (_cam != null) _cam.Render();
        }

        void Update()
        {
            // une image rendue par changement, pas une de plus
            if (_cam != null) { _cam.enabled = _sale; _sale = false; }
        }

        /// <summary>Le point visé sur l'écran, en pixels de toile, ou false
        /// si le rayon manque la dalle.</summary>
        public bool Viser(Ray ray, float portee, out Vector2 px, out Vector3 impact)
        {
            px = default; impact = default;
            if (panneau != null && Application.isPlaying)
            {
                if (!panneau.Viser(ray, portee, out Vector2 uv, out impact)) return false;
                px = new Vector2(uv.x * W, (1f - uv.y) * H);
                return true;
            }
            var t = Surface != null ? Surface : transform;
            var plan = new Plane(t.forward, t.position);
            if (!plan.Raycast(ray, out float d) || d > portee || d < 0f) return false;
            // la face avant seulement, celle que regarde la salle
            if (Vector3.Dot(ray.direction, t.forward) <= 0f) return false;
            impact = ray.GetPoint(d);
            var l = t.InverseTransformPoint(impact);
            px = Surface != null ? new Vector2((l.x + 0.5f) * W, (0.5f - l.y) * H)
                                 : new Vector2(l.x + W / 2f, H / 2f - l.y);
            return px.x >= 0 && px.y >= 0 && px.x <= W && px.y <= H;
        }

        /// <summary>Un point de la toile, en coordonnées monde de l'interface
        /// (et non de la dalle) : c'est là que vivent les boutons.</summary>
        public Vector3 PointToile(Vector2 px) =>
            transform.TransformPoint(new Vector3(px.x - W / 2f, H / 2f - px.y, 0f));

        bool _demarre;

        /// <summary>Fabrique l'interface et lit la bibliothèque. Appelé par
        /// Start, ou par l'aperçu de l'éditeur.</summary>
        public void Demarrer()
        {
            if (_demarre) return;
            _demarre = true;
            // `??` ne voit pas le « faux nul » d'un champ Unity vide : test explicite.
            AmphiUI.Regular = regular;
            AmphiUI.Semibold = semibold != null ? semibold : regular;
            AmphiUI.Bold = bold != null ? bold : AmphiUI.Semibold;
            Construire();
            _biblio.Actualiser();
            if (!_biblio.DeLaCategorie(_categorie).Any())
                _categorie = AmphiBibliotheque.Categories.FirstOrDefault(c => _biblio.DeLaCategorie(c).Any()) ?? "Cours";
            Aller(Vue.Accueil);
        }

        void OnDestroy()
        {
            _pdf?.Dispose();
            foreach (var t in _cache.Values) Detruire(t);
            foreach (var t in _couvertures.Values) Detruire(t);
            if (_rt != null) _rt.Release();
            AmphiBouton.Anime -= Sale;
        }

        const float RayonVerre = 56f;

        void Construire()
        {
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            // Les glyphes sont rastérisés à la taille du corps multipliée par
            // ce facteur : 2 garde un texte net vu à quelques mètres.
            scaler.dynamicPixelsPerUnit = 3f;     // x 1,5 de suréchantillonnage, x 2 de marge
            _toile = (RectTransform)transform;
            _toile.sizeDelta = new Vector2(W, H);
            _toile.pivot = new Vector2(0.5f, 0.5f);

            if (panneau != null)
            {
                // UNE VITRE FUMEE. La salle se devine derrière, le texte reste
                // sur un fond sombre et franc.
                //
                // 0,97 ET NON 0,86. Le mélange se fait en LINEAIRE : les 14 %
                // d'une salle claire, reconvertis pour l'écran, faisaient un
                // gris moyen, et les gradins traversaient le texte (aperçu du
                // 2026-09-29). À 0,97, mesuré tel quel dans la texture, la
                // salle ne laisse plus que des formes sombres : un verre fumé.
                var verre = AmphiUI.Boite("Fond", _toile, 0, 0, W, H,
                                          new Color(AmphiTheme.Ecran.r, AmphiTheme.Ecran.g, AmphiTheme.Ecran.b, 0.97f),
                                          RayonVerre);
                var li = AmphiUI.Noeud("Lisere", verre.transform);
                AmphiUI.Remplir(li);
                var a = li.gameObject.AddComponent<Image>();
                a.sprite = AmphiUI.Anneau; a.type = Image.Type.Sliced;
                a.pixelsPerUnitMultiplier = 32f / RayonVerre;
                a.color = new Color(1f, 0.96f, 0.9f, 0.16f);
                a.raycastTarget = false;
            }
            else
            {
                var fond = AmphiUI.Boite("Fond", _toile, 0, 0, W, H, Color.white);
                fond.sprite = AmphiUI.Degrade;      // dégradé doux, plus clair en haut
            }
            AmphiBouton.Anime += Sale;
            _vAccueil = AmphiUI.Noeud("Accueil", _toile); AmphiUI.Placer(_vAccueil, 0, 0, W, H);
            _vChargement = AmphiUI.Noeud("Chargement", _toile); AmphiUI.Placer(_vChargement, 0, 0, W, H);
            _vDocument = AmphiUI.Noeud("Document", _toile); AmphiUI.Placer(_vDocument, 0, 0, W, H);

            // point du rayon laser, toujours au-dessus du reste
            var p = AmphiUI.Boite("PointLaser", _toile, 0, 0, 30, 30, new Color(0.95f, 0.93f, 0.89f), 15);
            _point = p.rectTransform;
            _point.pivot = new Vector2(0.5f, 0.5f);
            p.gameObject.SetActive(false);
        }

        /// <summary>Place le point du laser, en pixels de toile ; null le cache.</summary>
        public void Point(Vector2? px, bool presse)
        {
            if (panneau != null && Application.isPlaying)
            {
                panneau.Point(px.HasValue ? new Vector2(px.Value.x / W, 1f - px.Value.y / H) : (Vector2?)null, presse);
                return;
            }
            if (_pointMonde != null)
            {
                _pointMonde.gameObject.SetActive(px.HasValue);
                if (!px.HasValue) return;
                float e = Surface.lossyScale.x / W;
                var local = new Vector3(px.Value.x / W - 0.5f, 0.5f - px.Value.y / H, 0f);
                _pointMonde.position = Surface.TransformPoint(local) - Surface.forward * 0.006f;
                _pointMonde.localScale = Vector3.one * ((presse ? 22f : 30f) * e);
                var c = presse ? AmphiTheme.CheneClair : new Color(0.95f, 0.93f, 0.89f);
                if (_matPoint.HasProperty("_BaseColor")) _matPoint.SetColor("_BaseColor", c);
                return;
            }
            if (_point == null) return;
            _point.gameObject.SetActive(px.HasValue);
            if (!px.HasValue) return;
            _point.anchoredPosition = new Vector2(px.Value.x, -px.Value.y);
            float d = presse ? 22f : 30f;
            _point.sizeDelta = new Vector2(d, d);
            _point.GetComponent<Image>().color = presse ? AmphiTheme.CheneClair : new Color(0.95f, 0.93f, 0.89f);
            _point.SetAsLastSibling();
        }

        /// <summary>Destroy en jeu, DestroyImmediate dans l'éditeur à l'arrêt
        /// (l'aperçu), où Destroy est refusé.</summary>
        static void Detruire(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void Vider(RectTransform vue)
        {
            for (int i = vue.childCount - 1; i >= 0; i--) Detruire(vue.GetChild(i).gameObject);
        }

        void Aller(Vue v)
        {
            VueCourante = v;
            if (v != Vue.Document) PleinEcran = false;
            Redessiner();
        }

        public void Redessiner()
        {
            _sale = true;
            cibles.Clear();
            _vAccueil.gameObject.SetActive(VueCourante == Vue.Accueil);
            _vChargement.gameObject.SetActive(VueCourante == Vue.Chargement);
            _vDocument.gameObject.SetActive(VueCourante == Vue.Document);
            if (VueCourante == Vue.Accueil) DessinerAccueil();
            if (VueCourante == Vue.Chargement) DessinerChargement();
            if (VueCourante == Vue.Document) DessinerDocument();
            Change?.Invoke();
        }

        /// <summary>
        /// Ce que projette le grand écran : la page courante du dernier
        /// document ouvert, même quand le panneau est revenu à la
        /// bibliothèque. Null si aucun document, ou si la page n'est pas
        /// encore rendue ; <paramref name="pdf"/> dit alors lequel des deux.
        /// </summary>
        public Texture2D PageProjetee(out int page, out float rapport, out bool retournee, out object pdf)
        {
            page = _page; rapport = 16f / 9f; retournee = false; pdf = _pdf;
            if (_pdf == null) return null;
            rapport = _pdf.Rapports[_page];
            retournee = _pdf.Retournee;
            return _cache.TryGetValue(_page, out var t) ? t : null;
        }

        AmphiBouton B(Transform p, float x, float y, float l, float h, string s,
                      AmphiBouton.Style st, System.Action a, float r = 20, int t = 38,
                      Icone? ic = null, bool icDroite = false)
        {
            var b = AmphiUI.Bouton("Bouton", p, x, y, l, h, s, st, a, r, t, ic, icDroite);
            cibles.Add(b);
            return b;
        }

        // ============================================================ ACCUEIL
        void DessinerAccueil()
        {
            var v = _vAccueil; Vider(v);
            const float M = 72;

            // --- barre du haut : logo, état, action principale
            var logo = Resources.Load<Texture2D>("Logo/logo_amphitraining");
            if (logo != null)
            {
                var li = AmphiUI.Noeud("Logo", v).gameObject.AddComponent<RawImage>();
                li.texture = logo;
                li.raycastTarget = false;
                float lh = 76f, lw = lh * logo.width / logo.height;
                AmphiUI.Placer(li.rectTransform, M, M - 4, lw, lh);
            }
            int total = _biblio.documents.Count, casque = _biblio.documents.Count(d => !d.EnLigne);
            Puce(v, W - M - 600 - 24, M + 6, $"{casque} dans le casque  ·  {total - casque} en ligne",
                 Icone.Casque, gauche: false);
            B(v, W - M - 600, M - 8, 600, 108, "Charger un document", AmphiBouton.Style.Principal,
              () => { _etat = "choix"; _message = null; Aller(Vue.Chargement); }, 54, 40, Icone.Plus);

            // --- titre de séance
            AmphiUI.Texte("Surtitre", v, M, 172, 1500, 40, surtitre, 30, AmphiUI.Semibold, AmphiTheme.Chene);
            AmphiUI.Texte("Seance", v, M, 206, 1300, 104, titreSeance, 92, AmphiUI.Bold, AmphiTheme.Marbre,
                          ajuster: true);
            AmphiUI.Texte("SousTitre", v, M, 312, 1300, 54, sousTitre, 40, AmphiUI.Regular, AmphiTheme.Feutre,
                          ajuster: true);

            // --- contrôle segmenté des catégories
            float sy = 392, sh = 108, sl = 3 * 330 + 2 * 8 + 16;
            AmphiUI.Boite("Segments", v, M, sy, sl, sh, AmphiTheme.Surface1, sh / 2);
            float x = M + 8;
            foreach (var c in AmphiBibliotheque.Categories)
            {
                int n = _biblio.DeLaCategorie(c).Count();
                var s = B(v, x, sy + 8, 330, sh - 16, $"{c}  <color=#8c8377>{n}</color>", AmphiBouton.Style.Segment,
                          () => { _categorie = c; _pageCartes = 0; Redessiner(); }, (sh - 16) / 2, 38);
                s.Selection = c == _categorie;
                x += 338;
            }

            var docs = _biblio.DeLaCategorie(_categorie).ToList();
            int pages = Mathf.Max(1, Mathf.CeilToInt(docs.Count / 4f));
            _pageCartes = Mathf.Clamp(_pageCartes, 0, pages - 1);
            if (pages > 1)
            {
                var p = B(v, W - M - 228, sy + 6, 100, 100, null, AmphiBouton.Style.Secondaire,
                          () => { _pageCartes--; Redessiner(); }, 50, 38, Icone.Gauche);
                p.Actif = _pageCartes > 0;
                var n = B(v, W - M - 100, sy + 6, 100, 100, null, AmphiBouton.Style.Secondaire,
                          () => { _pageCartes++; Redessiner(); }, 50, 38, Icone.Droite);
                n.Actif = _pageCartes < pages - 1;
                AmphiUI.Texte("PageCartes", v, W - M - 460, sy + 30, 210, 50,
                              $"{_pageCartes + 1} / {pages}", 34, AmphiUI.Semibold, AmphiTheme.Feutre,
                              TextAnchor.MiddleRight);
            }

            // --- cartes, 4 colonnes
            const float G = 36;
            float cy = 540, ch = H - 52 - cy, cl = (W - 2 * M - 3 * G) / 4f;
            if (docs.Count == 0)
            {
                var vide = AmphiUI.Panneau("Vide", v, M, cy, W - 2 * M, ch, AmphiTheme.Surface1, 32);
                AmphiUI.IconeImage(vide.transform, Icone.Fichier, (W - 2 * M) / 2 - 48, 48, 96, AmphiTheme.Feutre2);
                AmphiUI.Texte("VideTitre", vide.transform, 60, 168, W - 2 * M - 120, 60,
                              $"Aucun document dans « {_categorie} »", 50, AmphiUI.Bold, AmphiTheme.Marbre,
                              TextAnchor.UpperCenter, ajuster: true);
                AmphiUI.Texte("VideAide", vide.transform, 200, 240, W - 2 * M - 400, 120,
                              "Déposez vos PDF dans le dossier Presentations du casque, ou ajoutez un lien "
                              + "en ligne, puis touchez « Charger un document ».",
                              34, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.UpperCenter, ajuster: true);
                return;
            }
            int i = 0;
            foreach (var d in docs.Skip(_pageCartes * 4).Take(4))
            {
                float cx = M + i * (cl + G);
                var carte = B(v, cx, cy, cl, ch, null, AmphiBouton.Style.Carte, () => Ouvrir(d), 30);
                float vh = ch - 12 - 186;          // la vignette prend ce que le texte laisse
                var vig = AmphiUI.Boite("Vignette", carte.transform, 12, 12, cl - 24, vh, AmphiTheme.Miniature, 20);
                Couverture(d, vig.transform, cl - 24, vh);
                AmphiUI.Texte("Titre", carte.transform, 30, vh + 26, cl - 60, 84, d.titre, 38,
                              AmphiUI.Semibold, AmphiTheme.Marbre, TextAnchor.UpperLeft, ajuster: true);
                string meta = d.EnLigne ? (d.Disponible ? "En ligne, gardé" : "En ligne") : "Casque";
                if (d.pages > 0) meta = d.pages + " p.  ·  " + meta;
                float lp = Pastille(carte.transform, 30, ch - 76, d.categorie, 50);
                float mx = 30 + lp + 18;
                AmphiUI.Texte("Meta", carte.transform, mx, ch - 76, cl - mx - 24, 50,
                              meta, 30, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.MiddleLeft, ajuster: true);
                i++;
            }
        }

        /// <summary>Pastille de catégorie ; rend la largeur prise.</summary>
        float Pastille(Transform p, float x, float y, string s, float h = 56)
        {
            var mesure = AmphiUI.Texte("Mesure", p, 0, 0, 1000, h, s, 30, AmphiUI.Semibold, AmphiTheme.Marbre);
            float l = Mathf.Ceil(mesure.preferredWidth) + 48;
            Detruire(mesure.gameObject);
            var im = AmphiUI.Boite("Pastille", p, x, y, l, h, AmphiTheme.Sarcelle, h / 2);
            var t = AmphiUI.Texte("Texte", im.transform, 0, 0, l, h, s, 30, AmphiUI.Semibold,
                                  AmphiTheme.Marbre, TextAnchor.MiddleCenter);
            AmphiUI.Remplir(t.rectTransform);
            return l;
        }

        /// <summary>Une information discrète avec son icône, alignée à droite
        /// sur <paramref name="xDroite"/> si <paramref name="gauche"/> est faux.</summary>
        void Puce(Transform p, float xDroite, float y, string s, Icone ic, bool gauche)
        {
            var t = AmphiUI.Texte("Puce", p, 0, y, 900, 56, s, 30, AmphiUI.Regular, AmphiTheme.Feutre,
                                  TextAnchor.MiddleLeft);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            float l = Mathf.Ceil(t.preferredWidth);
            float x0 = gauche ? xDroite : xDroite - l - 46;
            AmphiUI.IconeImage(p, ic, x0, y + 12, 32, AmphiTheme.Feutre2);
            AmphiUI.Placer(t.rectTransform, x0 + 46, y, l + 4, 56);
        }

        /// <summary>Première page du document en vignette, rendue une fois.</summary>
        void Couverture(AmphiDocument d, Transform parent, float l, float h)
        {
            if (!d.Disponible)
            {
                AmphiUI.IconeImage(parent, d.EnLigne ? Icone.Globe : Icone.Fichier, l / 2 - 44, h / 2 - 44, 88,
                                   AmphiTheme.Feutre2);
                return;
            }
            var ri = AmphiUI.Noeud("Couverture", parent).gameObject.AddComponent<RawImage>();
            ri.raycastTarget = false;
            if (_couvertures.TryGetValue(d.fichier, out var t) && t != null) Poser(ri, t, l - 32, h - 32, 16, 16);
            else
            {
                ri.enabled = false;
                StartCoroutine(RendreCouverture(d, ri, l - 32, h - 32));
            }
        }

        IEnumerator RendreCouverture(AmphiDocument d, RawImage ri, float l, float h)
        {
            var pdf = AmphiPdf.Ouvrir(d.fichier, out _);
            if (pdf == null) yield break;
            d.pages = pdf.Pages;
            Texture2D t = null;
            yield return pdf.Rendre(0, 640, x => t = x);
            bool ret = pdf.Retournee;
            pdf.Dispose();
            if (t == null) yield break;
            _couvertures[d.fichier] = t;
            if (ri == null) yield break;
            ri.enabled = true;
            _sale = true;
            ri.uvRect = ret ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            Poser(ri, t, l, h, 16, 16);
        }

        /// <summary>Ajuste une texture au plus grand dans un rectangle,
        /// centrée : la même règle que le document, en petit.</summary>
        static void Poser(RawImage ri, Texture2D t, float l, float h, float x0, float y0)
        {
            ri.texture = t;
            float r = t.width / (float)t.height;
            float w = l, hh = w / r;
            if (hh > h) { hh = h; w = hh * r; }
            AmphiUI.Placer(ri.rectTransform, x0 + (l - w) / 2, y0 + (h - hh) / 2, w, hh);
        }

        // ========================================================= CHARGEMENT
        void DessinerChargement()
        {
            var v = _vChargement; Vider(v);
            const float M = 72;
            B(v, M, M - 4, 112, 100, null, AmphiBouton.Style.Secondaire,
              () => { _biblio.Actualiser(); Aller(Vue.Accueil); }, 50, 38, Icone.Retour);
            AmphiUI.Texte("Titre", v, M + 140, M + 2, 1400, 90, "Charger un document", 76, AmphiUI.Bold,
                          AmphiTheme.Marbre, TextAnchor.UpperLeft, ajuster: true);

            // --- panneau gauche : le casque
            float py = 212, ph = H - M - py + 20, gl = 1150;
            var g = AmphiUI.Panneau("Casque", v, M, py, gl, ph, AmphiTheme.Surface1, 32).transform;
            AmphiUI.IconeImage(g, Icone.Casque, 44, 44, 56, AmphiTheme.Chene);
            AmphiUI.Texte("H", g, 118, 40, 640, 64, "Dans le casque", 50, AmphiUI.Bold, AmphiTheme.Marbre,
                          TextAnchor.UpperLeft, ajuster: true);
            var locaux = _biblio.DuCasque.ToList();
            AmphiUI.Texte("Chemin", g, 44, 110, gl - 88, 40,
                          $"{locaux.Count} PDF  ·  dossier Presentations", 30,
                          AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
            var act = AmphiUI.Bouton("Actualiser", g, gl - 44 - 280, 36, 280, 88, "Actualiser",
                                     AmphiBouton.Style.Fantome, () => { _biblio.Actualiser(); Redessiner(); },
                                     44, 32, Icone.Actualiser);
            cibles.Add(act);
            const int parPage = 5;
            int pagesF = Mathf.Max(1, Mathf.CeilToInt(locaux.Count / (float)parPage));
            _pageFichiers = Mathf.Clamp(_pageFichiers, 0, pagesF - 1);
            float ly = 172;
            if (locaux.Count == 0)
                AmphiUI.Texte("Aucun", g, 44, ly + 20, gl - 88, 220,
                              "Aucun PDF pour l'instant. Branchez le casque au PC et copiez vos fichiers dans "
                              + "Presentations, rangés si vous voulez dans Cours, Conference ou Presentation.",
                              34, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
            foreach (var d in locaux.Skip(_pageFichiers * parPage).Take(parPage))
            {
                var l = AmphiUI.Bouton("Fichier", g, 28, ly, gl - 56, 100, null, AmphiBouton.Style.Ligne,
                                       () => Ouvrir(d), 22);
                cibles.Add(l);
                var tuile = AmphiUI.Boite("Tuile", l.transform, 18, 18, 64, 64, AmphiTheme.Chene, 16);
                AmphiUI.IconeImage(tuile.transform, Icone.Fichier, 12, 12, 40, AmphiTheme.EncreChene);
                AmphiUI.Texte("Nom", l.transform, 104, 8, gl - 56 - 104 - 110, 50, d.titre, 36,
                              AmphiUI.Semibold, AmphiTheme.Marbre, TextAnchor.MiddleLeft, ajuster: true);
                AmphiUI.Texte("Info", l.transform, 104, 56, gl - 56 - 104 - 110, 36,
                              $"{d.categorie}  ·  {AmphiBibliotheque.Taille(d.octets)}  ·  {d.date:dd/MM}",
                              28, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
                AmphiUI.IconeImage(l.transform, Icone.Droite, gl - 56 - 84, 26, 48, AmphiTheme.Chene);
                ly += 112;
            }
            if (pagesF > 1) Pagination(g, 44, ph - 116, gl - 88, _pageFichiers, pagesF, k => { _pageFichiers = k; Redessiner(); });

            // --- panneau droit : en ligne
            float dx = M + gl + 36, dl = W - M - dx;
            var d2 = AmphiUI.Panneau("EnLigne", v, dx, py, dl, ph, AmphiTheme.Surface1, 32).transform;
            AmphiUI.IconeImage(d2, Icone.Globe, 44, 44, 56, AmphiTheme.SarcelleClaire);
            AmphiUI.Texte("H", d2, 118, 40, dl - 160, 64, "En ligne", 50, AmphiUI.Bold, AmphiTheme.Marbre,
                          TextAnchor.UpperLeft, ajuster: true);

            var liens = _biblio.EnLigneListe.ToList();
            float ry = 132;
            const int parPageL = 3;
            int pagesL = Mathf.Max(1, Mathf.CeilToInt(liens.Count / (float)parPageL));
            _pageLiens = Mathf.Clamp(_pageLiens, 0, pagesL - 1);
            if (liens.Count == 0)
            {
                AmphiUI.Texte("AucunLien", d2, 44, ry, dl - 88, 170,
                              "Aucun lien. Ajoutez-en dans liens.txt, à côté des PDF : « Cours | Titre | adresse ». "
                              + "Google Slides, Drive, GitHub et Dropbox sont reconnus.",
                              32, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
                ry += 180;
            }
            foreach (var d in liens.Skip(_pageLiens * parPageL).Take(parPageL))
            {
                var l = AmphiUI.Bouton("Lien", d2, 28, ry, dl - 56, 100, null, AmphiBouton.Style.Ligne,
                                       () => Charger(d), 22);
                l.Selection = d == _telecharge && _etat != "choix";
                cibles.Add(l);
                AmphiUI.Texte("Nom", l.transform, 30, 8, dl - 56 - 150, 50, d.titre, 34, AmphiUI.Semibold,
                              AmphiTheme.Marbre, TextAnchor.MiddleLeft, ajuster: true);
                AmphiUI.Texte("Info", l.transform, 30, 56, dl - 56 - 150, 36,
                              d.categorie + (d.Disponible ? "  ·  déjà dans le casque" : ""),
                              28, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
                AmphiUI.IconeImage(l.transform, d.Disponible ? Icone.Droite : Icone.Telecharger,
                                   dl - 56 - 84, 26, 48, AmphiTheme.Chene);
                ry += 112;
            }
            if (pagesL > 1) { Pagination(d2, 44, ry, dl - 88, _pageLiens, pagesL, k => { _pageLiens = k; Redessiner(); }); ry += 112; }

#if UNITY_EDITOR || UNITY_STANDALONE
            // Adresse tapée au clavier : possible au poste, pas dans le casque.
            AmphiUI.Texte("Lbl", d2, 44, ry + 12, dl - 88, 40, "Ou tapez une adresse, puis Entrée",
                          30, AmphiUI.Semibold, AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
            var champ = AmphiUI.Boite("Champ", d2, 28, ry + 60, dl - 56, 96, AmphiTheme.Miniature, 22);
            var tc = AmphiUI.Texte("Url", champ.transform, 28, 0, dl - 112, 96,
                                   string.IsNullOrEmpty(_url) ? "https://" : _url + "|", 32, AmphiUI.Regular,
                                   string.IsNullOrEmpty(_url) ? AmphiTheme.Feutre2 : AmphiTheme.Marbre,
                                   TextAnchor.MiddleLeft, ajuster: true);
            ry += 172;
#endif
            // état du téléchargement
            float sy = ph - 232;
            if (_etat == "cours" || _etat == "fini")
            {
                string t = _etat == "fini" ? "Téléchargement terminé" : "Téléchargement en cours";
                AmphiUI.Texte("Etat", d2, 44, sy, dl - 440, 50, t, 36, AmphiUI.Semibold, AmphiTheme.Marbre,
                              TextAnchor.MiddleLeft, ajuster: true);
                AmphiUI.Texte("Nums", d2, dl - 400, sy, 356, 50,
                              _etat == "fini" ? AmphiBibliotheque.Taille(_recu)
                                              : $"{Mathf.RoundToInt(_avance * 100)} %  ·  {AmphiBibliotheque.Taille(_recu)}",
                              30, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.MiddleRight, ajuster: true);
                AmphiUI.Boite("Piste", d2, 44, sy + 66, dl - 88, 20, AmphiTheme.Surface3, 10);
                AmphiUI.Boite("Barre", d2, 44, sy + 66, Mathf.Max(20, (dl - 88) * (_etat == "fini" ? 1f : _avance)), 20,
                              _etat == "fini" ? AmphiTheme.Termine : AmphiTheme.SarcelleClaire, 10);
                if (_etat == "cours")
                    B(d2, dl - 44 - 300, sy + 118, 300, 96, "Annuler", AmphiBouton.Style.Fantome, Annuler, 48, 34);
                else
                    B(d2, dl - 44 - 480, sy + 118, 480, 96, "Ouvrir le document", AmphiBouton.Style.Principal,
                      () => Ouvrir(_telecharge), 48, 36, Icone.Droite, true);
            }
            else if (_etat == "erreur")
            {
                var e = AmphiUI.Boite("Erreur", d2, 28, sy - 30, dl - 56, 240, AmphiTheme.ErreurFond, 24).transform;
                var ico = AmphiUI.Boite("Ico", e, 30, 30, 64, 64, AmphiTheme.Erreur, 32);
                AmphiUI.Texte("I", ico.transform, 0, 0, 64, 64, "!", 42, AmphiUI.Bold,
                              new Color(0.16f, 0.07f, 0.05f), TextAnchor.MiddleCenter);
                AmphiUI.Texte("Msg", e, 120, 26, dl - 56 - 150, 190, _message, 32, AmphiUI.Regular,
                              new Color(0.93f, 0.86f, 0.83f), TextAnchor.UpperLeft, ajuster: true);
            }
        }

        void Pagination(Transform p, float x, float y, float l, int k, int n, System.Action<int> aller)
        {
            var a = AmphiUI.Bouton("Prec", p, x, y, 96, 96, null, AmphiBouton.Style.Secondaire, () => aller(k - 1),
                                   48, 38, Icone.Gauche);
            a.Actif = k > 0; cibles.Add(a);
            var b = AmphiUI.Bouton("Suiv", p, x + l - 96, y, 96, 96, null, AmphiBouton.Style.Secondaire, () => aller(k + 1),
                                   48, 38, Icone.Droite);
            b.Actif = k < n - 1; cibles.Add(b);
            AmphiUI.Texte("N", p, x + 110, y, l - 220, 96, $"{k + 1} / {n}", 34, AmphiUI.Semibold,
                          AmphiTheme.Feutre, TextAnchor.MiddleCenter);
        }

        void Charger(AmphiDocument d)
        {
            if (d.Disponible) { Ouvrir(d); return; }
            if (_etat == "cours") return;
            _telecharge = d; _etat = "cours"; _avance = 0; _recu = 0;
            Redessiner();
            _tache = StartCoroutine(_biblio.Telecharger(d,
                (a, o) =>
                {
                    bool change = Mathf.Abs(a - _avance) > 0.01f;
                    _avance = a; _recu = o;
                    if (change && VueCourante == Vue.Chargement) Redessiner();
                },
                err =>
                {
                    _tache = null;
                    if (err == null) { _etat = "fini"; _recu = d.octets; }
                    else { _etat = "erreur"; _message = err; }
                    if (VueCourante == Vue.Chargement) Redessiner();
                }));
        }

        void Annuler()
        {
            if (_tache != null) StopCoroutine(_tache);
            _tache = null; _etat = "choix";
            Redessiner();
        }

        /// <summary>Saisie d'adresse au clavier du PC (éditeur et poste).</summary>
        public void Saisir(string texte, bool valider, bool effacer)
        {
            if (VueCourante != Vue.Chargement) return;
            if (effacer && _url.Length > 0) _url = _url.Substring(0, _url.Length - 1);
            _url += texte;
            if (valider && _url.StartsWith("http"))
            {
                var d = _biblio.AjouterLien(_url, _categorie);
                _url = "";
                Charger(d);
                return;
            }
            Redessiner();
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------ aperçu
        /// <summary>Pour les vues de contrôle rendues en mode batch : ouvre
        /// un PDF et rend ses pages sans coroutine (elles ne tournent pas
        /// hors du mode jeu).</summary>
        public void Apercu(string fichier, int page, bool plein)
        {
            Demarrer();
            var d = new AmphiDocument
            {
                titre = AmphiBibliotheque.Titre(System.IO.Path.GetFileNameWithoutExtension(fichier)),
                categorie = "Cours", fichier = fichier,
            };
            Ouvrir(d);
            if (_pdf == null) { Debug.LogError("[Amphi] aperçu : " + _message); return; }
            _page = Mathf.Clamp(page, 0, _pdf.Pages - 1);
            for (int i = _page - 1; i <= _page + 1; i++)
            {
                if (i < 0 || i >= _pdf.Pages) continue;
                int k = i;
                var e = _pdf.Rendre(k, 2048, t => _cache[k] = t);
                while (e.MoveNext()) { }
            }
            PleinEcran = plein;
            Redessiner();
        }

        public void ApercuVue(Vue v) { Demarrer(); Aller(v); }

        /// <summary>Rend la page courante et ses voisines sans coroutine,
        /// puis prévient : ce que fait RendreDiffere en jeu.</summary>
        public void ApercuRendrePages()
        {
            if (_pdf == null) return;
            for (int i = _page - 1; i <= _page + 1; i++)
            {
                if (i < 0 || i >= _pdf.Pages || _cache.ContainsKey(i)) continue;
                int k = i;
                var e = _pdf.Rendre(k, 2048, t => _cache[k] = t);
                while (e.MoveNext()) { }
            }
            _enCours.Clear();
            Change?.Invoke();
        }
#endif

        // =========================================================== DOCUMENT
        void Ouvrir(AmphiDocument d)
        {
            if (d == null) return;
            if (!d.Disponible) { _etat = "choix"; Aller(Vue.Chargement); Charger(d); return; }
            _pdf?.Dispose();
            foreach (var t in _cache.Values) Detruire(t);
            _cache.Clear(); _enCours.Clear();
            _pdf = AmphiPdf.Ouvrir(d.fichier, out string err);
            if (_pdf == null)
            {
                _etat = "erreur"; _message = "Impossible d'ouvrir ce document : " + err;
                Aller(Vue.Chargement);
                return;
            }
            _doc = d; _doc.pages = _pdf.Pages; _page = 0;
            Aller(Vue.Document);
            DocumentOuvert?.Invoke(d);
        }

        public void Tourner(int delta)
        {
            // Le panneau peut être à la bibliothèque : la projection, elle,
            // montre encore le document, et le pointeur sur le grand écran
            // doit pouvoir en tourner les pages.
            if (_pdf == null) return;
            int p = Mathf.Clamp(_page + delta, 0, _pdf.Pages - 1);
            if (p == _page) return;
            _page = p;
            if (VueCourante == Vue.Document) Redessiner();
            else { Prerendre(0f); Change?.Invoke(); }
        }

        public void BasculerPleinEcran()
        {
            if (VueCourante != Vue.Document) return;
            PleinEcran = !PleinEcran;
            Redessiner();
        }

        /// <summary>Échap : sortir du plein écran, puis revenir à l'accueil.</summary>
        public void Retour()
        {
            if (VueCourante == Vue.Document && PleinEcran) { PleinEcran = false; Redessiner(); }
            else if (VueCourante != Vue.Accueil) { _biblio.Actualiser(); Aller(Vue.Accueil); }
        }

        /// <summary>La règle de la fiche : document ajusté au plus grand,
        /// centré, bandes égales de part et d'autre.</summary>
        public static Rect Ajuster(float rapport, bool plein, out float bande)
        {
            float M = plein ? 0 : 40, G = plein ? 0 : 32, B = plein ? 0 : 340;
            float zx = M + B + G, zw = W - 2 * zx, zh = H - 2 * M;
            float w = zw, h = w / rapport;
            if (h > zh) { h = zh; w = h * rapport; }
            w = Mathf.Round(w); h = Mathf.Round(h);
            float x = Mathf.Round((W - w) / 2), y = Mathf.Round((H - h) / 2);
            bande = plein ? 0 : x - M - G;
            return new Rect(x, y, w, h);
        }

        void DessinerDocument()
        {
            var v = _vDocument; Vider(v);
            int n = _pdf.Pages;
            float rapport = _pdf.Rapports[_page];
            var r = Ajuster(rapport, PleinEcran, out float bande);
            if (PleinEcran) AmphiUI.Boite("Fond", v, 0, 0, W, H, AmphiTheme.Plein, panneau != null ? RayonVerre : 0f);

            // la page, posée sur une ombre douce
            AmphiUI.Ombre(v, r.x, r.y, r.width, r.height, 14f, PleinEcran ? 0f : 1.1f);
            var cadre = AmphiUI.Boite("Page", v, r.x, r.y, r.width, r.height, Color.white);
            var ri = AmphiUI.Noeud("Rendu", cadre.transform).gameObject.AddComponent<RawImage>();
            AmphiUI.Remplir(ri.rectTransform);
            ri.raycastTarget = false;
            AfficherPage(ri, _page, r.width);

            if (PleinEcran)
            {
                B(v, r.x, r.y, r.width / 2, r.height, null, AmphiBouton.Style.Zone, () => Tourner(-1), 0);
                B(v, r.x + r.width / 2, r.y, r.width / 2, r.height, null, AmphiBouton.Style.Zone, () => Tourner(1), 0);
                var q = B(v, W - 40 - 110, 40, 110, 110, null, AmphiBouton.Style.Secondaire,
                          BasculerPleinEcran, 55, 38, Icone.Reduire);
                var pc = AmphiUI.Boite("Compteur", v, W - 40 - 220, H - 40 - 80, 220, 80,
                                       new Color(0.08f, 0.07f, 0.06f, 0.85f), 40);
                AmphiUI.Texte("N", pc.transform, 0, 0, 220, 80, $"{_page + 1} / {n}", 36, AmphiUI.Semibold,
                              AmphiTheme.Marbre, TextAnchor.MiddleCenter);
                Prerendre(r.width);
                return;
            }

            const float M = 40;
            bool large = bande >= 500;
            float bh = H - 2 * M;

            // --- panneau gauche : le document, les commandes, Précédent
            var gp = AmphiUI.Panneau("BandeG", v, M, M, bande, bh, AmphiTheme.Surface1, 30, 0.8f);
            var g = gp.transform;
            const float P = 26;
            float li = bande - 2 * P;
            float lp = Pastille(g, P, P, _doc.categorie, 52);
            AmphiUI.Texte("Titre", g, P, P + 76, li, 210, _doc.titre, 46, AmphiUI.Bold, AmphiTheme.Marbre,
                          TextAnchor.UpperLeft, ajuster: true);
            string origine = _doc.EnLigne ? "En ligne" : "Dans le casque";
            AmphiUI.Texte("Info", g, P, P + 296, li, 44, $"{n} pages  ·  {origine}", 30, AmphiUI.Regular,
                          AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
            float hb = 100, eb = 14;
            float ay = bh - P - 124 - eb - (large ? hb : 2 * hb + eb);

            // --- la salle : un clic, et les agents savent de quoi il s'agit
            if (Configurer != null)
            {
                bool prete = SalleConfigureePourDocument != null && SalleConfigureePourDocument();
                B(g, P, ay - hb - eb, li, hb, prete ? "Reconfigurer la salle" : "Configurer la salle",
                  prete ? AmphiBouton.Style.Fantome : AmphiBouton.Style.Principal,
                  () => { Configurer(_doc); Redessiner(); }, 50, 34, Icone.Actualiser);
                string etat = EtatSalle != null ? EtatSalle() : null;
                if (!string.IsNullOrEmpty(etat))
                    AmphiUI.Texte("EtatSalle", g, P, ay - hb - eb - 96, li, 84, etat, 26, AmphiUI.Regular,
                                  prete ? AmphiTheme.Termine : AmphiTheme.Feutre, TextAnchor.LowerLeft, ajuster: true);
            }
            if (large)
            {
                float l2 = (li - eb) / 2;
                B(g, P, ay, l2, hb, "Accueil", AmphiBouton.Style.Fantome, Retour, 50, 36, Icone.Maison);
                B(g, P + l2 + eb, ay, l2, hb, "Plein écran", AmphiBouton.Style.Fantome, BasculerPleinEcran, 50, 36, Icone.Agrandir);
            }
            else
            {
                B(g, P, ay, li, hb, "Accueil", AmphiBouton.Style.Fantome, Retour, 50, 36, Icone.Maison);
                B(g, P, ay + hb + eb, li, hb, "Plein écran", AmphiBouton.Style.Fantome, BasculerPleinEcran, 50, 36, Icone.Agrandir);
            }
            var prec = B(g, P, bh - P - 124, li, 124, "Précédent", AmphiBouton.Style.Secondaire, () => Tourner(-1),
                         62, 40, Icone.Gauche);
            prec.Actif = _page > 0;

            // --- panneau droit : compteur, voisines, Suivant
            var dp = AmphiUI.Panneau("BandeD", v, W - M - bande, M, bande, bh, AmphiTheme.Surface1, 30, 0.8f);
            var d = dp.transform;
            AmphiUI.Texte("Lbl", d, P, P, li, 36, "PAGE", 28, AmphiUI.Semibold, AmphiTheme.Feutre2);
            var c = AmphiUI.Texte("Compteur", d, P, P + 38, li, 104,
                                  $"{_page + 1}<color=#8c8377><size=56>  / {n}</size></color>", 92,
                                  AmphiUI.Bold, AmphiTheme.Marbre, TextAnchor.UpperLeft, ajuster: true);
            AmphiUI.Boite("Piste", d, P, P + 154, li, 12, AmphiTheme.Surface3, 6);
            AmphiUI.Boite("Prog", d, P, P + 154, Mathf.Max(12, li * (_page + 1) / n), 12, AmphiTheme.Chene, 6);
            float ty = P + 190;
            if (large)
            {
                AmphiUI.Texte("Aide", d, P, ty, li, 80,
                              panneau != null ? "Joystick ou balayage de la main, ici ou sur le grand écran."
                                              : "Visez l'écran et poussez le joystick à gauche ou à droite.",
                              30, AmphiUI.Regular, AmphiTheme.Feutre, TextAnchor.UpperLeft, ajuster: true);
                ty += 92;
            }
            float th = bh - P - 124 - 24 - ty;
            Voisines(d, P, ty, li, th);
            var suiv = B(d, P, bh - P - 124, li, 124, "Suivant", AmphiBouton.Style.Principal, () => Tourner(1),
                         62, 40, Icone.Droite, true);
            suiv.Actif = _page < n - 1;

            Prerendre(r.width);
        }

        /// <summary>Les deux pages voisines, en ligne ou en colonne : on garde
        /// la disposition qui donne la plus grande miniature.</summary>
        void Voisines(Transform p, float x, float y, float l, float h)
        {
            const float lab = 44, gap = 18, bd = 10;
            float rap = _pdf.Rapports[_page];
            float enLigne = Mathf.Min((l - gap) / 2 - bd, (h - lab - bd) * rap);
            float enCol = Mathf.Min(l - bd, ((h - gap) / 2 - lab - bd) * rap);
            bool ligne = enLigne > enCol;
            float tw = Mathf.Floor(ligne ? enLigne : enCol), th = Mathf.Round(tw / rap);
            int[] k = { _page - 1, _page + 1 };
            for (int j = 0; j < 2; j++)
            {
                float sx = ligne ? x + j * ((l + gap) / 2) + ((l - gap) / 2 - tw - bd) / 2 : x + (l - tw - bd) / 2;
                float sy = ligne ? y : y + j * (th + bd + lab + gap);
                int i = k[j];
                if (i < 0 || i >= _pdf.Pages)
                {
                    var vide = AmphiUI.Boite("Vide", p, sx, sy, tw + bd, th + bd, AmphiTheme.Surface2, 14);
                    AmphiUI.Texte("T", vide.transform, 12, 0, tw + bd - 24, th + bd, i < 0 ? "Début" : "Fin",
                                  30, AmphiUI.Semibold, AmphiTheme.Feutre2, TextAnchor.MiddleCenter, ajuster: true);
                    continue;
                }
                int cible = i;
                var b = B(p, sx, sy, tw + bd, th + bd, null, AmphiBouton.Style.Carte, () => { _page = cible; Redessiner(); }, 14);
                var ri = AmphiUI.Noeud("Mini", b.transform).gameObject.AddComponent<RawImage>();
                AmphiUI.Placer(ri.rectTransform, bd / 2, bd / 2, tw, th);
                ri.raycastTarget = false;
                AfficherPage(ri, i, tw);
                AmphiUI.Texte("L", p, sx, sy + th + bd + 6, tw + bd, lab,
                              (j == 0 ? "Page " : "Page ") + (i + 1), 30,
                              AmphiUI.Semibold, AmphiTheme.Feutre, TextAnchor.UpperCenter, ajuster: true);
            }
        }

        /// <summary>Largeur de rendu : la page tient 1636 px de toile au plus ;
        /// on la rend un peu plus fine que la toile pour garder le texte net
        /// quand on s'approche de l'écran.</summary>
        static int LargeurRendu(float largeurToile) =>
            Mathf.Clamp(Mathf.RoundToInt(largeurToile * Surech * 1.05f / 64f) * 64, 640, 2688);

        int _largeurCache;

        void AfficherPage(RawImage ri, int i, float largeurToile)
        {
            ri.uvRect = _pdf.Retournee ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            if (_cache.TryGetValue(i, out var t) && t != null) { ri.texture = t; ri.color = Color.white; return; }
            ri.color = new Color(1, 1, 1, 0);
            Demander(i);
        }

        void Prerendre(float largeurToile)
        {
            _largeurCache = Mathf.Max(_largeurCache, LargeurRendu(Ajuster(1.78f, true, out _).width));
            Demander(_page); Demander(_page + 1); Demander(_page - 1);
            // on ne garde que la page et ses deux voisines de chaque côté
            foreach (var k in _cache.Keys.Where(k => Mathf.Abs(k - _page) > 2).ToList())
            {
                Detruire(_cache[k]);
                _cache.Remove(k);
            }
        }

        void Demander(int i)
        {
            if (_pdf == null || i < 0 || i >= _pdf.Pages || _cache.ContainsKey(i) || _enCours.Contains(i)) return;
            _enCours.Add(i);
            StartCoroutine(RendreDiffere(i, _pdf));
        }

        /// <summary>Une page par image au plus : un rendu PDF coûte quelques
        /// dizaines de millisecondes, deux dans la même image se verraient
        /// dans le casque.</summary>
        IEnumerator RendreDiffere(int i, AmphiPdf pdf)
        {
            yield return null;
            int largeur = _largeurCache > 0 ? _largeurCache : 2048;
            // une page portrait n'a pas besoin de la largeur d'une diapositive
            largeur = Mathf.RoundToInt(largeur * Mathf.Min(1f, pdf.Rapports[i] / 1.78f * 1.4f));
            largeur = Mathf.Clamp(largeur, 640, 2688);
            Texture2D t = null;
            yield return pdf.Rendre(i, largeur, x => t = x);
            if (pdf != _pdf) { if (t != null) Detruire(t); yield break; }   // document changé entre-temps
            _enCours.Remove(i);
            if (t == null) yield break;
            _cache[i] = t;
            if (VueCourante == Vue.Document && Mathf.Abs(i - _page) <= 1) Redessiner();
            else if (i == _page) Change?.Invoke();
        }
    }
}
