// Le grand écran de la salle : ce que voient les étudiants.
//
// Demande d'Idrissa du 2026-09-29 : la salle ne voit plus l'interface. Le
// présentateur conduit la séance sur son panneau (AmphiPanneauPresentateur),
// et le grand écran ne montre que ce qu'il projette, comme un vidéoprojecteur
// branché sur son ordinateur :
//
//   un document ouvert   sa page courante, au plus grand, sur fond noir ;
//   aucun document       l'écran d'attente : logo, séance, titre.
//
// Revenir à la bibliothèque sur le panneau NE COUPE PAS la projection : la
// dernière page reste à l'écran pendant qu'on cherche le document suivant,
// comme en cours.
//
// LA NETTETE. Même chaîne que l'ancien écran, qui avait fait ses preuves vue
// du fond de salle : une toile rendue dans une texture de 3690 x 1500 à
// mipmaps, trilinéaire, anisotrope x 8, biais -0,4, et une caméra qui ne
// rend que sur changement. La page n'est plus une vignette dans une
// interface : elle occupe la dalle entière, 1,5 fois plus de pixels qu'avant.
//
// LE POINTEUR. Viser le grand écran pose un point, comme un pointeur laser.
// La gâchette y passe à la page suivante, le joystick et le balayage tournent
// les pages : c'est AmphiPointeurEcran qui en décide.

using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Amphi
{
    public class AmphiProjection : MonoBehaviour
    {
        public AmphiEcranPresentation ecran;

        [Header("Dalle, posée par le constructeur")]
        public float largeur = 7.63f;
        public float hauteur = 3.10f;
        [Tooltip("Matière opaque non éclairée de la dalle.")]
        public Material matiereSurface;
        [Tooltip("Amphi/PanneauVerre : pour le point du pointeur.")]
        public Material matiereVerre;

        [Tooltip("Durée du fondu entre deux pages, en secondes.")]
        public float fondu = 0.22f;

        const float W = AmphiEcranPresentation.W, H = AmphiEcranPresentation.H;
        const float Surech = 1.5f;
        const float Echelle = 0.001f;      // 1 mm par pixel de toile, hors de vue

        Transform _surface, _point;
        Material _matPoint;
        RectTransform _toile;
        Camera _cam;
        RenderTexture _rt;
        bool _sale = true;
        float _finFondu;

        // ce qui est à l'écran
        Texture _texture;
        int _page = -1;
        object _pdf;
        RawImage _avant, _apres;

        /// <summary>La page à l'écran, -2 à l'écran d'attente.</summary>
        public int PageAffichee => _page;
        CanvasGroup _groupeApres;

        void Start()
        {
            if (ecran == null) ecran = FindFirstObjectByType<AmphiEcranPresentation>();
            if (ecran == null) { enabled = false; return; }
            Monter();
        }

        /// <summary>Monte la dalle, la toile et sa caméra, puis s'abonne à
        /// l'écran du présentateur. Appelé par Start, ou par l'aperçu de
        /// l'éditeur ; une seule fois.</summary>
        public void Monter()
        {
            if (_toile != null) return;
            ecran.Demarrer();                      // les polices, avant tout texte
            MonterDalleEtToile();
            ecran.Change += Actualiser;
            Actualiser();
        }

        /// <summary>Pour l'aperçu de l'éditeur, où Update ne tourne pas.</summary>
        public void RendreMaintenant()
        {
            if (_groupeApres != null) _groupeApres.alpha = 1f;
            Canvas.ForceUpdateCanvases();
            if (_cam != null) _cam.Render();
        }

        void OnDestroy()
        {
            if (ecran != null) ecran.Change -= Actualiser;
            if (_rt != null) _rt.Release();
        }

        void Update()
        {
            if (_cam == null) return;
            bool anime = Time.time < _finFondu;
            if (anime && _groupeApres != null)
                _groupeApres.alpha = Mathf.SmoothStep(0f, 1f, 1f - (_finFondu - Time.time) / fondu);
            else if (_groupeApres != null && _groupeApres.alpha < 1f) { _groupeApres.alpha = 1f; _sale = true; }
            _cam.enabled = _sale || anime;
            _sale = false;
        }

        // ------------------------------------------------------------ montage
        void MonterDalleEtToile()
        {
            _rt = new RenderTexture(Mathf.RoundToInt(W * Surech), Mathf.RoundToInt(H * Surech), 16,
                                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Projection",
                useMipMap = true,
                autoGenerateMips = true,
                anisoLevel = 8,
                filterMode = FilterMode.Trilinear,
                wrapMode = TextureWrapMode.Clamp,
                mipMapBias = -0.4f,
            };
            _rt.Create();

            // la dalle
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "SURFACE_PROJECTION";
            Detruire(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.localScale = new Vector3(largeur, hauteur, 1f);
            var mat = matiereSurface != null ? new Material(matiereSurface)
                                             : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.mainTexture = _rt;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _rt);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _surface = q.transform;

            // la toile, loin sous la salle, sur le calque UI
            var go = new GameObject("PROJECTION_TOILE", typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetPositionAndRotation(new Vector3(0f, -800f, 0f), Quaternion.identity);
            go.transform.localScale = Vector3.one * Echelle;
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            _toile = (RectTransform)go.transform;
            _toile.sizeDelta = new Vector2(W, H);
            _toile.pivot = new Vector2(0.5f, 0.5f);

            var cg = new GameObject("CAM_PROJECTION");
            cg.transform.SetPositionAndRotation(go.transform.position - Vector3.forward * 2f, Quaternion.identity);
            _cam = cg.AddComponent<Camera>();
            _cam.orthographic = true;
            _cam.orthographicSize = H * Echelle / 2f;
            _cam.aspect = W / H;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 5f;
            _cam.cullingMask = 1 << go.layer;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = AmphiTheme.Plein;
            _cam.allowHDR = false;
            _cam.allowMSAA = false;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.targetTexture = _rt;
            _cam.enabled = false;

            // le point du pointeur, devant la dalle
            var p = new GameObject("POINT_PROJECTION");
            p.transform.SetParent(transform, false);
            var mf = p.AddComponent<MeshFilter>();
            mf.sharedMesh = q.GetComponent<MeshFilter>().sharedMesh;
            var mp = p.AddComponent<MeshRenderer>();
            _matPoint = matiereVerre != null ? new Material(matiereVerre)
                                             : new Material(Shader.Find("Amphi/PanneauVerre"));
            _matPoint.SetTexture("_BaseMap", AmphiPanneauPresentateur.Disque);
            _matPoint.renderQueue = 3001;
            mp.sharedMaterial = _matPoint;
            mp.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mp.receiveShadows = false;
            p.SetActive(false);
            _point = p.transform;
        }

        // ------------------------------------------------------------ contenu
        /// <summary>Appelé à chaque changement de l'écran du présentateur ; ne
        /// redessine que si ce qui est projeté a changé.</summary>
        void Actualiser()
        {
            if (_toile == null) return;
            var t = ecran.PageProjetee(out int page, out float rapport, out bool retournee, out object pdf);
            if (pdf == null)
            {
                if (_pdf != null || _page != -2) Attente();
                return;
            }
            // page pas encore rendue : on garde l'ancienne à l'écran plutôt
            // que de montrer un noir le temps du rendu
            if (t == null) return;
            if (t == _texture && page == _page && pdf == _pdf) return;
            bool memeDocument = pdf == _pdf && _avant != null;
            _texture = t; _page = page; _pdf = pdf;
            Page(t, rapport, retournee, memeDocument);
        }

        void Vider()
        {
            for (int i = _toile.childCount - 1; i >= 0; i--) Detruire(_toile.GetChild(i).gameObject);
            _avant = _apres = null;
            _groupeApres = null;
        }

        /// <summary>L'écran d'attente : aucun document n'est projeté.</summary>
        void Attente()
        {
            Vider();
            _texture = null; _page = -2; _pdf = null;
            AmphiUI.Boite("Fond", _toile, 0, 0, W, H, AmphiTheme.Ecran).sprite = AmphiUI.Degrade;
            float y = 250;
            var logo = Resources.Load<Texture2D>("Logo/logo_amphitraining");
            if (logo != null)
            {
                var li = AmphiUI.Noeud("Logo", _toile).gameObject.AddComponent<RawImage>();
                li.texture = logo;
                li.raycastTarget = false;
                float lh = 170f, lw = lh * logo.width / logo.height;
                AmphiUI.Placer(li.rectTransform, (W - lw) / 2, y, lw, lh);
                y += lh + 70;
            }
            AmphiUI.Texte("Surtitre", _toile, 200, y, W - 400, 50, ecran.surtitre, 40, AmphiUI.Semibold,
                          AmphiTheme.Chene, TextAnchor.UpperCenter);
            AmphiUI.Texte("Seance", _toile, 200, y + 58, W - 400, 140, ecran.titreSeance, 120, AmphiUI.Bold,
                          AmphiTheme.Marbre, TextAnchor.UpperCenter, ajuster: true);
            _finFondu = 0f;
            _sale = true;
        }

        /// <summary>Une page au plus grand, sur fond noir. Dans le même
        /// document, la nouvelle page arrive en fondu sur l'ancienne.</summary>
        void Page(Texture t, float rapport, bool retournee, bool fondre)
        {
            var r = AmphiEcranPresentation.Ajuster(rapport, true, out _);
            RawImage ancienne = fondre ? _apres : null;
            if (ancienne != null)
            {
                // on garde la page affichée comme fond du fondu, on jette le reste
                for (int i = _toile.childCount - 1; i >= 0; i--)
                {
                    var c = _toile.GetChild(i);
                    if (c != ancienne.transform.parent) Detruire(c.gameObject);
                }
                ancienne.transform.parent.SetAsFirstSibling();
                _avant = ancienne;
            }
            else
            {
                Vider();
                AmphiUI.Boite("Fond", _toile, 0, 0, W, H, AmphiTheme.Plein).transform.SetAsFirstSibling();
            }

            var groupe = AmphiUI.Noeud("Page", _toile);
            AmphiUI.Remplir(groupe);
            _groupeApres = groupe.gameObject.AddComponent<CanvasGroup>();
            AmphiUI.Boite("Blanc", groupe, r.x, r.y, r.width, r.height, Color.white);
            var ri = AmphiUI.Noeud("Rendu", groupe).gameObject.AddComponent<RawImage>();
            AmphiUI.Placer(ri.rectTransform, r.x, r.y, r.width, r.height);
            ri.raycastTarget = false;
            ri.texture = t;
            ri.uvRect = retournee ? new Rect(0, 1, 1, -1) : new Rect(0, 0, 1, 1);
            _apres = ri;

            if (ancienne != null && fondu > 0f && Application.isPlaying)
            {
                _groupeApres.alpha = 0f;
                _finFondu = Time.time + fondu;
                StartCoroutine(Nettoyer(ancienne.transform.parent.gameObject, fondu + 0.05f));
            }
            else
            {
                _finFondu = 0f;
                if (ancienne != null) Detruire(ancienne.transform.parent.gameObject);
            }
            _sale = true;
        }

        static void Detruire(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        IEnumerator Nettoyer(GameObject go, float apres)
        {
            yield return new WaitForSeconds(apres);
            if (go != null && _apres != null && go != _apres.transform.parent.gameObject) Detruire(go);
            _sale = true;
        }

        // ------------------------------------------------------------ pointeur
        /// <summary>Le rayon touche-t-il la dalle, face à la salle ?</summary>
        public bool Viser(Ray ray, float portee, out Vector2 uv, out Vector3 impact)
        {
            uv = default; impact = default;
            if (_surface == null) return false;
            var plan = new Plane(_surface.forward, _surface.position);
            if (!plan.Raycast(ray, out float d) || d > portee || d < 0f) return false;
            if (Vector3.Dot(ray.direction, _surface.forward) <= 0f) return false;
            impact = ray.GetPoint(d);
            var l = _surface.InverseTransformPoint(impact);
            uv = new Vector2(l.x + 0.5f, l.y + 0.5f);
            return uv.x >= 0f && uv.y >= 0f && uv.x <= 1f && uv.y <= 1f;
        }

        /// <summary>Le point du pointeur laser ; null le cache.</summary>
        public void Point(Vector2? uv, bool presse)
        {
            if (_point == null) return;
            if (_point.gameObject.activeSelf != uv.HasValue) _point.gameObject.SetActive(uv.HasValue);
            if (!uv.HasValue) return;
            var local = new Vector3(uv.Value.x - 0.5f, uv.Value.y - 0.5f, 0f);
            _point.position = _surface.TransformPoint(local) - _surface.forward * 0.006f;
            _point.rotation = _surface.rotation;
            // un point de pointeur laser : 9 cm sur une dalle de 7,6 m,
            // assez pour se voir du dernier rang
            float d = presse ? 0.07f : 0.09f;
            _point.localScale = new Vector3(d, d, 1f);
        }
    }
}
