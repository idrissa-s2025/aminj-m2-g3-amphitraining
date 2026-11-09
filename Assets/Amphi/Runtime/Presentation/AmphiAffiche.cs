// Le petit écran portrait : une image à la fois. Affiche, programme,
// visuel de conférence.
//
// D'OU VIENNENT LES IMAGES.
//   - Celles du projet, dans Assets/Amphi/Resources/Affiches : l'affiche
//     d'accueil, le programme, l'affiche de conférence. Elles partent dans le
//     build et s'affichent dès le premier lancement.
//   - Les vôtres : des PNG ou JPG déposés dans le dossier Affiches, à côté des
//     présentations, dans le casque :
//       Quest 3 > Android/data/com.amphitraining.vr/files/Presentations/Affiches
//     Dès qu'il y en a une, elles remplacent celles du projet.
//
// L'image garde ses proportions : elle est ajustée au plus grand dans la dalle
// de 1,70 x 2,60 m, sur le fond de l'écran. S'il y en a plusieurs, elles se
// succèdent en fondu ; viser l'écran et presser la gâchette passe à la
// suivante.
//
// NETTETE. L'image est affichée telle quelle, à mipmaps et filtrée en
// anisotrope : c'est ce qui la garde lisible depuis le fond de la salle, où
// la dalle ne couvre plus que quelques dizaines de pixels du casque.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Amphi
{
    public class AmphiAffiche : MonoBehaviour
    {
        [Tooltip("Largeur et hauteur de la dalle, en mètres (posées par le constructeur).")]
        public float largeur = 1.70f;
        public float hauteur = 2.60f;

        [Tooltip("Matière opaque non éclairée (posée par le constructeur).")]
        public Material matiere;

        [Tooltip("Secondes entre deux images ; 0 pour ne jamais changer seul.")]
        public float intervalle = 15f;

        public static string Dossier => Path.Combine(AmphiBibliotheque.Dossier, "Affiches");

        readonly List<Texture2D> _images = new List<Texture2D>();
        readonly List<Texture2D> _chargees = new List<Texture2D>();
        Transform _image;
        Material _mat;
        int _courante;
        float _depuis;
        Coroutine _fondu;

        void Start() => Monter();

        bool _monte;

        static void Detruire(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        /// <summary>Fabrique la dalle et montre la première image. Appelé par
        /// Start, ou par l'aperçu de l'éditeur.</summary>
        public void Monter()
        {
            if (_monte) return;
            _monte = true;
            var fond = GameObject.CreatePrimitive(PrimitiveType.Quad);
            fond.name = "FOND_AFFICHE";
            Detruire(fond.GetComponent<Collider>());
            fond.transform.SetParent(transform, false);
            fond.transform.localScale = new Vector3(largeur, hauteur, 1f);
            var matFond = Matiere();
            if (matFond.HasProperty("_BaseColor")) matFond.SetColor("_BaseColor", AmphiTheme.Ecran);
            fond.GetComponent<MeshRenderer>().sharedMaterial = matFond;

            var img = GameObject.CreatePrimitive(PrimitiveType.Quad);
            img.name = "IMAGE_AFFICHE";
            Detruire(img.GetComponent<Collider>());
            img.transform.SetParent(transform, false);
            img.transform.localPosition = new Vector3(0f, 0f, -0.003f);   // devant le fond
            _mat = Matiere();
            img.GetComponent<MeshRenderer>().sharedMaterial = _mat;
            _image = img.transform;

            Charger();
            Montrer(0);
        }

        Material Matiere()
        {
            var m = matiere != null ? new Material(matiere)
                                    : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            return m;
        }

        /// <summary>Relit le dossier du casque, sinon prend les images du projet.</summary>
        public void Charger()
        {
            foreach (var t in _chargees) Detruire(t);
            _chargees.Clear();
            _images.Clear();
            try
            {
                Directory.CreateDirectory(Dossier);
                foreach (var f in Directory.GetFiles(Dossier)
                             .Where(f => f.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".jpg", System.StringComparison.OrdinalIgnoreCase)
                                      || f.EndsWith(".jpeg", System.StringComparison.OrdinalIgnoreCase))
                             .OrderBy(f => f, System.StringComparer.OrdinalIgnoreCase))
                {
                    var t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (t.LoadImage(File.ReadAllBytes(f))) { Regler(t); _chargees.Add(t); }
                    else Detruire(t);
                }
            }
            catch (System.Exception e) { Debug.LogWarning("[Amphi] affiches : " + e.Message); }

            _images.AddRange(_chargees);
            if (_images.Count == 0)
                _images.AddRange(Resources.LoadAll<Texture2D>("Affiches").OrderBy(t => t.name));
            foreach (var t in _images) Regler(t);
        }

        static void Regler(Texture2D t)
        {
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
        }

        void Update()
        {
            if (intervalle <= 0f || _images.Count < 2) return;
            _depuis += Time.deltaTime;
            if (_depuis >= intervalle) Suivante();
        }

        /// <summary>Passe à l'image suivante, en fondu.</summary>
        public void Suivante()
        {
            if (_images.Count == 0) return;
            _depuis = 0f;
            if (_fondu != null) StopCoroutine(_fondu);
            _fondu = StartCoroutine(Fondu((_courante + 1) % _images.Count));
        }

        IEnumerator Fondu(int suivante)
        {
            const float d = 0.35f;
            for (float t = 0; t < d; t += Time.deltaTime) { Teinte(1f - t / d); yield return null; }
            Montrer(suivante);
            for (float t = 0; t < d; t += Time.deltaTime) { Teinte(t / d); yield return null; }
            Teinte(1f);
            _fondu = null;
        }

        void Teinte(float k)
        {
            // Fondu par le fond de l'écran, et non par la transparence : la
            // matière reste opaque, donc sans tri ni surcoût de mélange.
            var c = Color.Lerp(AmphiTheme.Ecran, Color.white, k);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
        }

        void Montrer(int i)
        {
            _courante = i;
            if (_images.Count == 0) { _image.gameObject.SetActive(false); return; }
            var t = _images[i];
            _image.gameObject.SetActive(true);
            _mat.mainTexture = t;
            if (_mat.HasProperty("_BaseMap")) _mat.SetTexture("_BaseMap", t);
            // ajustée au plus grand, proportions gardées
            float r = t.width / (float)t.height;
            float w = largeur, h = w / r;
            if (h > hauteur) { h = hauteur; w = h * r; }
            _image.localScale = new Vector3(w, h, 1f);
        }

        /// <summary>Vrai si le rayon touche la face avant de la dalle.</summary>
        public bool Touche(Ray ray, float portee)
        {
            var plan = new Plane(transform.forward, transform.position);
            if (!plan.Raycast(ray, out float d) || d > portee || d < 0f) return false;
            if (Vector3.Dot(ray.direction, transform.forward) <= 0f) return false;
            var l = transform.InverseTransformPoint(ray.GetPoint(d));
            return Mathf.Abs(l.x) <= largeur / 2f && Mathf.Abs(l.y) <= hauteur / 2f;
        }
    }
}
