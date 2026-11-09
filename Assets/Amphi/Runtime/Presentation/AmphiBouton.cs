// Le système de design de l'écran de présentation : couleurs, ombres,
// icônes, textes qui tiennent dans leur cadre, et boutons animés.
//
// POURQUOI PAS LE SYSTEME D'EVENEMENTS D'UNITY. Il faudrait un raycaster pour
// manettes XR, c'est-à-dire le XR Interaction Toolkit, dont ce projet se passe
// volontairement (voir AmphiRig). Un écran plat se vise très bien à la main :
// on coupe son plan par le rayon de la manette, et on regarde quel rectangle
// contient le point. C'est ce que fait AmphiPointeurEcran ; ce composant-ci
// porte l'état, l'apparence et l'animation.
//
// DEUXIEME VERSION, 2026-09-24. Idrissa demandait une interface « moderne et
// très jolie », et que les textes ne débordent jamais de leurs boutons :
//   - des ombres douces sous les surfaces, qui donnent la profondeur qu'un
//     aplat seul n'a pas ;
//   - des icônes vectorielles dessinées ici, et non des flèches de police ;
//   - des transitions de couleur et un léger soulèvement des cartes au
//     survol, en 120 ms ;
//   - des libellés en « meilleur ajustement » : la taille baisse d'elle-même
//     jusqu'à ce que le texte tienne, jamais sous 60 % du corps prévu.

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Amphi
{
    public class AmphiBouton : MonoBehaviour
    {
        public enum Style { Principal, Secondaire, Fantome, Segment, Carte, Ligne, Zone }

        public Style style;
        public Image fond;
        public Image contour;
        public Text texte;
        public Image icone;
        public Action clic;

        /// <summary>Appelé tant qu'une transition est en cours : l'écran
        /// rendu en texture doit alors se redessiner à chaque image.</summary>
        public static event Action Anime;

        bool _actif = true, _survol, _presse, _selection;
        Color _fond, _contour, _texte;
        float _lever;
        bool _initialise;

        public bool Actif { get => _actif; set { _actif = value; Viser(true); } }
        public bool Selection { get => _selection; set { _selection = value; Viser(true); } }
        public RectTransform Rect => (RectTransform)transform;

        public void Survol(bool v) { if (_survol != v) { _survol = v; Viser(false); } }
        public void Presse(bool v) { if (_presse != v) { _presse = v; Viser(false); } }

        public void Peindre() => Viser(true);

        void Cibles(out Color f, out Color c, out Color t, out float lever)
        {
            f = Color.clear; c = Color.clear; t = AmphiTheme.Marbre; lever = 0f;
            bool s = _survol && _actif, p = _presse && _actif;
            switch (style)
            {
                case Style.Principal:
                    f = p ? AmphiTheme.CheneFonce : s ? AmphiTheme.CheneClair : AmphiTheme.Chene;
                    c = s ? AmphiTheme.Marbre : Color.clear;
                    t = AmphiTheme.EncreChene;
                    break;
                case Style.Secondaire:
                    f = p ? AmphiTheme.GunFonce : s ? AmphiTheme.GunClair : AmphiTheme.Gun;
                    c = p ? AmphiTheme.CheneFonce : s ? AmphiTheme.Chene : AmphiTheme.GunTrait;
                    break;
                case Style.Fantome:
                    f = p ? AmphiTheme.Surface2 : s ? AmphiTheme.Surface1 : new Color(1, 1, 1, 0f);
                    c = s ? AmphiTheme.Chene : AmphiTheme.Trait;
                    t = s ? AmphiTheme.Marbre : AmphiTheme.Feutre;
                    break;
                case Style.Segment:
                    f = _selection ? AmphiTheme.Sarcelle : p ? AmphiTheme.SarcelleFoncee
                        : s ? AmphiTheme.Surface2 : new Color(1, 1, 1, 0f);
                    c = _selection ? AmphiTheme.SarcelleClaire : s ? AmphiTheme.Chene : Color.clear;
                    t = _selection || s ? AmphiTheme.Marbre : AmphiTheme.Feutre;
                    break;
                case Style.Carte:
                    f = p ? AmphiTheme.GunFonce : s ? AmphiTheme.Surface2 : AmphiTheme.Surface1;
                    c = s ? AmphiTheme.Chene : AmphiTheme.Lisere;
                    lever = s && !p ? 1f : 0f;
                    break;
                case Style.Ligne:
                    f = _selection ? AmphiTheme.Sarcelle : p ? AmphiTheme.GunFonce
                        : s ? AmphiTheme.Surface3 : AmphiTheme.Surface2;
                    c = _selection ? AmphiTheme.SarcelleClaire : s ? AmphiTheme.Chene : Color.clear;
                    break;
            }
            if (!_actif && style != Style.Zone)
            {
                f = style == Style.Fantome || style == Style.Segment ? new Color(1, 1, 1, 0f) : AmphiTheme.DesactiveFond;
                c = AmphiTheme.DesactiveTrait;
                t = AmphiTheme.DesactiveTexte;
            }
        }

        void Viser(bool immediat)
        {
            if (fond == null) return;
            if (immediat || !_initialise)
            {
                Cibles(out _fond, out _contour, out _texte, out _lever);
                _initialise = true;
                Appliquer();
                return;
            }
            enabled = true;           // Update reprend la transition
        }

        void Update()
        {
            Cibles(out var f, out var c, out var t, out var l);
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.045f);   // ~120 ms à 95 %
            _fond = Color.Lerp(_fond, f, k);
            _contour = Color.Lerp(_contour, c, k);
            _texte = Color.Lerp(_texte, t, k);
            _lever = Mathf.Lerp(_lever, l, k);
            Appliquer();
            Anime?.Invoke();
            if (Proche(_fond, f) && Proche(_contour, c) && Proche(_texte, t) && Mathf.Abs(_lever - l) < 0.01f)
            {
                _fond = f; _contour = c; _texte = t; _lever = l;
                Appliquer();
                enabled = false;
            }
        }

        static bool Proche(Color a, Color b) =>
            Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.01f;

        void Appliquer()
        {
            if (style == Style.Zone) { fond.color = Color.clear; return; }
            fond.color = _fond;
            if (contour != null) { contour.color = _contour; contour.enabled = _contour.a > 0.01f; }
            if (texte != null) texte.color = _texte;
            if (icone != null) icone.color = _texte;
            // les cartes grossissent de 2 % au survol : elles se soulèvent
            transform.localScale = Vector3.one * (1f + 0.02f * _lever);
        }

        void OnEnable() { if (!_initialise) Viser(true); }
    }

    /// <summary>Les couleurs de la maquette, reprises de la salle.</summary>
    public static class AmphiTheme
    {
        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static readonly Color Ecran = H("#17140f");
        public static readonly Color EcranHaut = H("#221d17");
        public static readonly Color Plein = H("#0a0907");
        public static readonly Color Surface1 = H("#221e19");
        public static readonly Color Surface2 = H("#2c2721");
        public static readonly Color Surface3 = H("#383128");
        public static readonly Color Trait = H("#4a443b");
        public static readonly Color Lisere = H("#35302a");
        public static readonly Color Marbre = H("#f4efe6");
        public static readonly Color Feutre = H("#bfb4a4");
        public static readonly Color Feutre2 = H("#8c8377");
        public static readonly Color Chene = H("#d0a164");
        public static readonly Color CheneClair = H("#e3ba84");
        public static readonly Color CheneFonce = H("#a67a44");
        public static readonly Color EncreChene = H("#1c1409");
        public static readonly Color Sarcelle = H("#1f5a61");
        public static readonly Color SarcelleFoncee = H("#173d42");
        public static readonly Color SarcelleClaire = H("#6cb8be");
        public static readonly Color Gun = H("#2f3337");
        public static readonly Color GunClair = H("#3d4247");
        public static readonly Color GunFonce = H("#23262a");
        public static readonly Color GunTrait = H("#454b51");
        public static readonly Color DesactiveFond = H("#221f1b");
        public static readonly Color DesactiveTexte = H("#6a6258");
        public static readonly Color DesactiveTrait = H("#2e2a25");
        public static readonly Color Erreur = H("#e8836a");
        public static readonly Color ErreurFond = H("#3b221c");
        public static readonly Color Termine = H("#8cc49a");
        public static readonly Color Miniature = H("#0f0d0b");
        public static readonly Color Ombre = new Color(0f, 0f, 0f, 0.55f);
    }

    /// <summary>Les icônes, dessinées en traits arrondis.</summary>
    public enum Icone { Gauche, Droite, Plus, Maison, Agrandir, Reduire, Telecharger, Actualiser, Fichier, Globe, Casque, Retour }

    /// <summary>
    /// Fabrique des éléments d'interface en coordonnées de la maquette :
    /// pixels de toile, origine en haut à gauche, y vers le bas.
    /// </summary>
    public static class AmphiUI
    {
        public static Font Regular, Semibold, Bold;

        static Sprite _plein, _anneau, _ombre, _degrade;
        static readonly Dictionary<Icone, Sprite> _icones = new Dictionary<Icone, Sprite>();

        /// <summary>Rectangle arrondi plein, neuf tranches, rayon 32 px.</summary>
        public static Sprite Plein => _plein ??= Arrondi(false);

        /// <summary>Contour arrondi de 3 px, neuf tranches.</summary>
        public static Sprite Anneau => _anneau ??= Arrondi(true);

        /// <summary>Ombre douce : un rectangle arrondi flouté, neuf tranches.</summary>
        public static Sprite OmbreDouce => _ombre ??= FaireOmbre();

        static Sprite Arrondi(bool anneau)
        {
            const int n = 96, r = 32;
            const float e = 3f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dedans = Dedans(x + 0.5f, y + 0.5f, n, r);
                    float a = Mathf.Clamp01(dedans + 0.5f);
                    if (anneau) a *= Mathf.Clamp01(e - dedans + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(r, r, r, r));
        }

        /// <summary>Distance signée au bord d'un carré arrondi, positive dedans.</summary>
        static float Dedans(float x, float y, int n, float r)
        {
            float cx = Mathf.Clamp(x, r, n - r), cy = Mathf.Clamp(y, r, n - r);
            float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
            if (d > 0f) return r - d;
            return r + Mathf.Min(Mathf.Min(x - r, n - r - x), Mathf.Min(y - r, n - r - y));
        }

        static Sprite FaireOmbre()
        {
            // 128 px, un rectangle arrondi de rayon 24 au centre, flou de 40 px
            const int n = 160, b = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // distance au rectangle intérieur [b, n-b]
                    float dx = Mathf.Max(b - (x + 0.5f), 0f, (x + 0.5f) - (n - b));
                    float dy = Mathf.Max(b - (y + 0.5f), 0f, (y + 0.5f) - (n - b));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Exp(-(d * d) / (2f * 22f * 22f));
                    px[y * n + x] = new Color32(0, 0, 0, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                                 SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        /// <summary>Dégradé vertical doux, pour le fond de l'écran.</summary>
        public static Sprite Degrade => _degrade ??= FaireDegrade();

        static Sprite FaireDegrade()
        {
            const int n = 256;
            var t = new Texture2D(4, n, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[4 * n];
            for (int y = 0; y < n; y++)
            {
                float k = Mathf.SmoothStep(0f, 1f, y / (n - 1f));      // y = 0 en bas
                Color c = Color.Lerp(AmphiTheme.Ecran, AmphiTheme.EcranHaut, k);
                for (int x = 0; x < 4; x++) px[y * 4 + x] = c;
            }
            t.SetPixels32(px);
            t.Apply(false, true);
            return Sprite.Create(t, new Rect(0, 0, 4, n), new Vector2(0.5f, 0.5f));
        }

        // ------------------------------------------------------------ icônes
        public static Sprite IconeSprite(Icone i)
        {
            if (_icones.TryGetValue(i, out var s) && s != null) return s;
            s = Dessiner(Traits(i));
            _icones[i] = s;
            return s;
        }

        /// <summary>Les traits de chaque icône, en coordonnées 0..1, y vers le bas.</summary>
        static List<Vector2[]> Traits(Icone i)
        {
            var L = new List<Vector2[]>();
            Vector2 P(float x, float y) => new Vector2(x, y);
            switch (i)
            {
                case Icone.Gauche: L.Add(new[] { P(.62f, .2f), P(.36f, .5f), P(.62f, .8f) }); break;
                case Icone.Droite: L.Add(new[] { P(.38f, .2f), P(.64f, .5f), P(.38f, .8f) }); break;
                case Icone.Retour:
                    L.Add(new[] { P(.8f, .5f), P(.22f, .5f) });
                    L.Add(new[] { P(.44f, .26f), P(.2f, .5f), P(.44f, .74f) }); break;
                case Icone.Plus: L.Add(new[] { P(.5f, .2f), P(.5f, .8f) }); L.Add(new[] { P(.2f, .5f), P(.8f, .5f) }); break;
                case Icone.Maison:
                    L.Add(new[] { P(.14f, .5f), P(.5f, .18f), P(.86f, .5f) });
                    L.Add(new[] { P(.26f, .42f), P(.26f, .84f), P(.74f, .84f), P(.74f, .42f) });
                    L.Add(new[] { P(.43f, .84f), P(.43f, .64f), P(.57f, .64f), P(.57f, .84f) }); break;
                case Icone.Agrandir:
                    L.Add(new[] { P(.18f, .4f), P(.18f, .18f), P(.4f, .18f) });
                    L.Add(new[] { P(.6f, .18f), P(.82f, .18f), P(.82f, .4f) });
                    L.Add(new[] { P(.82f, .6f), P(.82f, .82f), P(.6f, .82f) });
                    L.Add(new[] { P(.4f, .82f), P(.18f, .82f), P(.18f, .6f) }); break;
                case Icone.Reduire:
                    L.Add(new[] { P(.18f, .4f), P(.4f, .4f), P(.4f, .18f) });
                    L.Add(new[] { P(.6f, .18f), P(.6f, .4f), P(.82f, .4f) });
                    L.Add(new[] { P(.82f, .6f), P(.6f, .6f), P(.6f, .82f) });
                    L.Add(new[] { P(.4f, .82f), P(.4f, .6f), P(.18f, .6f) }); break;
                case Icone.Telecharger:
                    L.Add(new[] { P(.5f, .14f), P(.5f, .62f) });
                    L.Add(new[] { P(.3f, .44f), P(.5f, .64f), P(.7f, .44f) });
                    L.Add(new[] { P(.18f, .7f), P(.18f, .84f), P(.82f, .84f), P(.82f, .7f) }); break;
                case Icone.Actualiser:
                    L.Add(Arc(.5f, .52f, .3f, -40f, 250f, 14));
                    L.Add(new[] { P(.62f, .12f), P(.76f, .3f), P(.54f, .34f) }); break;
                case Icone.Fichier:
                    L.Add(new[] { P(.28f, .12f), P(.58f, .12f), P(.76f, .3f), P(.76f, .88f), P(.28f, .88f), P(.28f, .12f) });
                    L.Add(new[] { P(.58f, .12f), P(.58f, .3f), P(.76f, .3f) }); break;
                case Icone.Globe:
                    L.Add(Arc(.5f, .5f, .34f, 0f, 360f, 24));
                    L.Add(new[] { P(.16f, .5f), P(.84f, .5f) });
                    L.Add(Ellipse(.5f, .5f, .15f, .34f, 20)); break;
                case Icone.Casque:
                    L.Add(new[] { P(.14f, .36f), P(.86f, .36f), P(.86f, .68f), P(.6f, .68f), P(.5f, .56f),
                                  P(.4f, .68f), P(.14f, .68f), P(.14f, .36f) }); break;
            }
            return L;
        }

        static Vector2[] Arc(float cx, float cy, float r, float a0, float a1, int n)
        {
            var p = new Vector2[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(a0, a1, k / (float)n);
                p[k] = new Vector2(cx + r * Mathf.Cos(a), cy - r * Mathf.Sin(a));
            }
            return p;
        }

        static Vector2[] Ellipse(float cx, float cy, float rx, float ry, int n)
        {
            var p = new Vector2[n + 1];
            for (int k = 0; k <= n; k++)
            {
                float a = 2f * Mathf.PI * k / n;
                p[k] = new Vector2(cx + rx * Mathf.Cos(a), cy + ry * Mathf.Sin(a));
            }
            return p;
        }

        /// <summary>Rastérise des traits arrondis de 8 % d'épaisseur, bords
        /// anticrénelés, dans une texture de 128 px à mipmaps.</summary>
        static Sprite Dessiner(List<Vector2[]> traits)
        {
            const int n = 128;
            const float e = 0.085f * n / 2f;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var q = new Vector2(x + 0.5f, n - (y + 0.5f));
                    float d = float.MaxValue;
                    foreach (var tr in traits)
                        for (int k = 0; k + 1 < tr.Length; k++)
                            d = Mathf.Min(d, DistSegment(q, tr[k] * n, tr[k + 1] * n));
                    float a = Mathf.Clamp01(e - d + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            t.SetPixels32(px);
            t.Apply(true, true);
            return Sprite.Create(t, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }

        static float DistSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + t * ab);
        }

        // ---------------------------------------------------------- briques
        public static RectTransform Noeud(string nom, Transform parent)
        {
            var go = new GameObject(nom, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            return rt;
        }

        public static void Placer(RectTransform rt, float x, float y, float l, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(l, h);
        }

        public static void Remplir(RectTransform rt, float marge = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(marge, marge);
            rt.offsetMax = new Vector2(-marge, -marge);
        }

        public static Image Boite(string nom, Transform parent, float x, float y, float l, float h,
                                  Color c, float rayon = 0f)
        {
            var rt = Noeud(nom, parent);
            Placer(rt, x, y, l, h);
            var im = rt.gameObject.AddComponent<Image>();
            im.color = c;
            im.raycastTarget = false;
            if (rayon > 0f)
            {
                im.sprite = Plein;
                im.type = Image.Type.Sliced;
                im.pixelsPerUnitMultiplier = 32f / rayon;
            }
            return im;
        }

        /// <summary>Ombre portée douce sous un rectangle : décalée de
        /// <paramref name="dy"/> vers le bas, débordant de 40 px.</summary>
        public static Image Ombre(Transform parent, float x, float y, float l, float h,
                                  float dy = 10f, float force = 1f)
        {
            const float deb = 40f;
            var rt = Noeud("Ombre", parent);
            Placer(rt, x - deb, y - deb + dy, l + 2 * deb, h + 2 * deb);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = OmbreDouce;
            im.type = Image.Type.Sliced;
            im.pixelsPerUnitMultiplier = 64f / (deb + 24f);
            im.color = new Color(0, 0, 0, AmphiTheme.Ombre.a * force);
            im.raycastTarget = false;
            return im;
        }

        /// <summary>Une surface posée : ombre, fond arrondi, liseré clair.</summary>
        public static Image Panneau(string nom, Transform parent, float x, float y, float l, float h,
                                    Color c, float rayon = 28f, float elevation = 1f)
        {
            if (elevation > 0f) Ombre(parent, x, y, l, h, 12f * elevation, elevation);
            var im = Boite(nom, parent, x, y, l, h, c, rayon);
            var li = Noeud("Lisere", im.transform);
            Remplir(li);
            var a = li.gameObject.AddComponent<Image>();
            a.sprite = Anneau; a.type = Image.Type.Sliced;
            a.pixelsPerUnitMultiplier = 32f / rayon;
            a.color = new Color(1f, 0.96f, 0.9f, 0.06f);
            a.raycastTarget = false;
            return im;
        }

        public static Image IconeImage(Transform parent, Icone i, float x, float y, float taille, Color c)
        {
            var rt = Noeud("Icone", parent);
            Placer(rt, x, y, taille, taille);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = IconeSprite(i);
            im.color = c;
            im.raycastTarget = false;
            im.preserveAspect = true;
            return im;
        }

        /// <summary>
        /// Un texte. <paramref name="ajuster"/> : la taille baisse d'elle-même
        /// jusqu'à ce que le texte tienne dans sa boîte, jamais sous 60 %.
        /// </summary>
        public static Text Texte(string nom, Transform parent, float x, float y, float l, float h,
                                 string s, int taille, Font police, Color c,
                                 TextAnchor align = TextAnchor.UpperLeft, bool ajuster = false)
        {
            var rt = Noeud(nom, parent);
            Placer(rt, x, y, l, h);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = police;
            t.fontSize = taille;
            t.color = c;
            t.text = s;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.lineSpacing = 1.0f;
            t.raycastTarget = false;
            t.supportRichText = true;
            if (ajuster)
            {
                // le « meilleur ajustement » mesure dans la boîte : il lui faut
                // un débordement vertical tronqué, sinon il n'a rien à mesurer
                t.resizeTextForBestFit = true;
                t.resizeTextMaxSize = taille;
                t.resizeTextMinSize = Mathf.Max(18, Mathf.RoundToInt(taille * 0.6f));
                t.verticalOverflow = VerticalWrapMode.Truncate;
            }
            else
            {
                // Déborder, ne pas tronquer : le Text d'Unity efface TOUTE
                // ligne qui dépasse sa boîte, même d'un pixel.
                t.verticalOverflow = VerticalWrapMode.Overflow;
            }
            return t;
        }

        /// <summary>
        /// Un bouton : fond arrondi, contour, icône facultative et libellé
        /// ajusté à sa place. Le libellé ne déborde JAMAIS : il est borné à
        /// la largeur restante après l'icône et les marges, et réduit au
        /// besoin.
        /// </summary>
        public static AmphiBouton Bouton(string nom, Transform parent, float x, float y, float l, float h,
                                         string libelle, AmphiBouton.Style style, Action clic,
                                         float rayon = 20f, int taille = 38, Icone? icone = null,
                                         bool iconeADroite = false)
        {
            if (style == AmphiBouton.Style.Principal || style == AmphiBouton.Style.Carte)
                Ombre(parent, x, y, l, h, style == AmphiBouton.Style.Carte ? 14f : 8f,
                      style == AmphiBouton.Style.Carte ? 1f : 0.6f);
            var fond = Boite(nom, parent, x, y, l, h, Color.white, rayon);
            // pivot au centre pour que le soulèvement grossisse depuis le milieu
            var frt = fond.rectTransform;
            frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = new Vector2(x + l / 2f, -(y + h / 2f));
            var b = fond.gameObject.AddComponent<AmphiBouton>();
            b.style = style;
            b.fond = fond;
            b.clic = clic;
            if (style != AmphiBouton.Style.Zone)
            {
                var c = Noeud("Contour", fond.transform);
                Remplir(c);
                var im = c.gameObject.AddComponent<Image>();
                im.sprite = Anneau;
                im.type = Image.Type.Sliced;
                im.pixelsPerUnitMultiplier = 32f / Mathf.Max(1f, rayon);
                im.raycastTarget = false;
                b.contour = im;
            }
            float pad = Mathf.Min(32f, h * 0.3f);
            float ic = icone.HasValue ? Mathf.Round(h * 0.42f) : 0f;
            float gap = icone.HasValue && !string.IsNullOrEmpty(libelle) ? 14f : 0f;
            if (!string.IsNullOrEmpty(libelle))
            {
                float dispo = l - 2 * pad - ic - gap;
                b.texte = Texte("Libelle", fond.transform, 0, 0, dispo, h, libelle, taille,
                                Semibold, AmphiTheme.Marbre, TextAnchor.MiddleCenter, ajuster: true);
                b.texte.horizontalOverflow = HorizontalWrapMode.Wrap;
                // la largeur réelle du texte, pour centrer l'ensemble icône + texte
                float tl = Mathf.Min(dispo, b.texte.preferredWidth);
                float total = tl + ic + gap;
                float x0 = (l - total) / 2f;
                float tx = iconeADroite ? x0 : x0 + ic + gap;
                var trt = b.texte.rectTransform;
                trt.anchorMin = trt.anchorMax = new Vector2(0, 1);
                trt.pivot = new Vector2(0, 1);
                trt.anchoredPosition = new Vector2(tx, 0);
                trt.sizeDelta = new Vector2(tl + 2f, h);
                if (icone.HasValue)
                    b.icone = IconeImage(fond.transform, icone.Value,
                                         iconeADroite ? x0 + tl + gap : x0, (h - ic) / 2f, ic, AmphiTheme.Marbre);
            }
            else if (icone.HasValue)
            {
                b.icone = IconeImage(fond.transform, icone.Value, (l - ic) / 2f, (h - ic) / 2f, ic, AmphiTheme.Marbre);
            }
            b.Peindre();
            return b;
        }
    }
}
