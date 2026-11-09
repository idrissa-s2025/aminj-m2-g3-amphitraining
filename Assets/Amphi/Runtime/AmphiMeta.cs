// Lecture de Data/amphi_meta.json — le contrat entre Blender et Unity.
//
// Les noms de champs sont ceux du JSON, en français : JsonUtility apparie par
// nom exact, et renommer ici casserait silencieusement la lecture (le champ
// resterait à sa valeur par défaut, sans erreur). Le fichier est produit par
// blender/export_unity.py ; ne pas l'éditer à la main.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Amphi
{
    [Serializable]
    public class MetaTemoin
    {
        public string objet;
        public float[] pos;
        public float yaw_deg;
    }

    [Serializable]
    public class MetaSiege
    {
        public string id;
        public string bloc;
        public int rang;
        public float[] pos;
        public float yaw_deg;
    }

    [Serializable]
    public class MetaApparition
    {
        public string id;
        public string role;
        public float[] pos;
        public float yaw_deg;
    }

    [Serializable]
    public class MetaLumiere
    {
        public string name;
        public string type;             // SUN, AREA, POINT, SPOT
        public float[] position;
        public float[] rotation_euler_deg;
        public float[] color;
        public float energy_w;
        public float size;
        public float radius;
        public float angle_deg;
        public float spot_size_deg;
    }

    [Serializable]
    public class MetaEmission
    {
        public float[] color;
        public float strength;
    }

    [Serializable]
    public class MetaEquipement
    {
        public string nom;          // Ecran_Principal, Micro, Pupitre…
        public string genre;        // ecran, micro, mobilier, projecteur, luminaire
        public string objet;        // nom de l'objet dans le FBX : EQUIP_<nom>
        public float[] centre;      // repère Blender, mètres
        public float[] taille;      // encombrement, mètres
        public int triangles;
    }

    [Serializable]
    public class MetaMateriau
    {
        public string name;
        public float[] base_color;
        public float[] base_tint;
        public float metallic;
        public float smoothness;
        public float tiling;
        public float uv_rotation_deg;
        public MetaEmission emission;
        public string basecolor_map;
        // Réflectance moyenne de la carte ci-dessus, par canal, en linéaire.
        // Mesurée à l'export. Sans elle, l'albédo réel d'une surface texturée
        // est inconnu : `base_tint` n'en est que le multiplicateur.
        public float[] basecolor_mean;
        public string roughness_map;
        public string normal_map;
        public float normal_strength;
        public float rough_mul;
        public float rough_add;
        public float alpha;
        public bool transparent;

        public bool HasEmission =>
            emission != null && emission.strength > 0.001f && emission.color != null;
    }

    [Serializable]
    public class MetaEmmarchement
    {
        public int marches_par_gradin;
        public float giron_m;
        public float contremarche_m;
    }

    [Serializable]
    public class MetaSalle
    {
        public int places;
        public int rangs;
        public float giron_m;
        public float contremarche_m;
        public float hauteur_assise_m;
        public float hauteur_tablette_m;
        public float niveau_palier_m;
        public float hauteur_scene_m;
        public int valeur_c_mm;
        public MetaEmmarchement emmarchement;
    }

    [Serializable]
    public class MetaUnity
    {
        public string cible;
        public int frequence_hz;
        public int msaa;
        public float lightmap_resolution_px_par_unite;
        public string teleportation_layer;
        public string conseil;
    }

    [Serializable]
    public class AmphiMeta
    {
        public string format;
        public string genere_le;
        public string source;
        public MetaSalle salle;
        public List<MetaTemoin> temoins = new List<MetaTemoin>();
        public List<MetaSiege> sieges = new List<MetaSiege>();
        public List<MetaApparition> apparitions = new List<MetaApparition>();
        public List<MetaLumiere> lumieres = new List<MetaLumiere>();
        public List<MetaMateriau> materiaux = new List<MetaMateriau>();
        public List<MetaEquipement> equipements = new List<MetaEquipement>();
        public MetaUnity unity;

        public static AmphiMeta FromJson(string json)
        {
            var m = JsonUtility.FromJson<AmphiMeta>(json);
            if (m == null || m.sieges == null || m.sieges.Count == 0)
                throw new Exception("amphi_meta.json illisible ou vide");
            return m;
        }

        public MetaApparition Apparition(string id)
        {
            foreach (var a in apparitions)
                if (a.id == id) return a;
            return apparitions.Count > 0 ? apparitions[0] : null;
        }

        public static Vector3 V(float[] a) =>
            a == null || a.Length < 3 ? Vector3.zero : new Vector3(a[0], a[1], a[2]);

        public static Color C(float[] a, float alpha = 1f) =>
            a == null || a.Length < 3 ? Color.white : new Color(a[0], a[1], a[2], alpha);
    }

    /// <summary>
    /// Conversion du repère Blender (Z haut, droitier) vers Unity (Y haut,
    /// gaucher), DEDUITE des sièges témoins au lieu d'être codée en dur.
    ///
    /// L'exporteur laisse deux sièges à leur place réelle dans le FBX et note
    /// leurs coordonnées Blender dans le meta. On cherche, pour chaque axe
    /// d'Unity, l'axe de Blender et le signe qui rendent compte des deux
    /// témoins à la fois. Une salle exportée en miroir ne se voit pas sur une
    /// capture d'écran ; elle se voit une fois le casque sur la tête, quand la
    /// porte est du mauvais côté.
    /// </summary>
    public class AxisMap
    {
        public readonly int[] Src = { 0, 2, 1 };      // repli : (x, z, y)
        public readonly float[] Sign = { 1f, 1f, -1f };
        public bool Detected;

        /// <summary>
        /// Correction du sens de rotation, vérifiée sur le cap d'un témoin.
        /// Le signe se déduit du déterminant, mais la vérification coûte une
        /// comparaison d'angles : autant la faire plutôt que de la supposer.
        /// </summary>
        public float YawFlip = 1f;

        public Vector3 Pos(float[] blender)
        {
            var b = AmphiMeta.V(blender);
            return new Vector3(Sign[0] * b[Src[0]], Sign[1] * b[Src[1]],
                               Sign[2] * b[Src[2]]);
        }

        public Vector3 Up => new Vector3(Sign[0] * (Src[0] == 2 ? 1 : 0),
                                         Sign[1] * (Src[1] == 2 ? 1 : 0),
                                         Sign[2] * (Src[2] == 2 ? 1 : 0));

        /// <summary>Déterminant : négatif si la conversion change de main.</summary>
        public float Det
        {
            get
            {
                var m = new float[3, 3];
                for (int i = 0; i < 3; i++) m[i, Src[i]] = Sign[i];
                return m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
                     - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
                     + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            }
        }

        /// <summary>
        /// Rotation Unity correspondant à un cap autour du Z de Blender.
        /// La rotation devient, dans le nouveau repère, une rotation autour de
        /// l'image de l'axe Z, d'un angle changé de signe si la conversion
        /// inverse la main.
        /// </summary>
        public Quaternion Yaw(float blenderYawDeg)
        {
            var axis = Up;
            if (axis.sqrMagnitude < 0.5f) axis = Vector3.up;
            return Quaternion.AngleAxis(YawFlip * Det * blenderYawDeg, axis);
        }

        /// <summary>
        /// Déduit la conversion de deux témoins : leurs coordonnées Blender,
        /// et la position que le FBX importé leur donne réellement dans Unity.
        /// </summary>
        public static AxisMap Detect(Vector3[] blender, Vector3[] unity,
                                     float tol, out string rapport)
        {
            var map = new AxisMap();
            var lines = new List<string>();
            bool ok = blender.Length >= 2 && blender.Length == unity.Length;
            for (int i = 0; i < 3 && ok; i++)
            {
                int found = -1;
                float sign = 1f;
                int matches = 0;
                for (int j = 0; j < 3; j++)
                {
                    foreach (float s in new[] { 1f, -1f })
                    {
                        bool all = true;
                        for (int k = 0; k < blender.Length; k++)
                            all &= Mathf.Abs(unity[k][i] - s * blender[k][j]) <= tol;
                        if (!all) continue;
                        matches++;
                        found = j;
                        sign = s;
                    }
                }
                if (matches != 1)
                {
                    lines.Add($"axe Unity {i} : {matches} correspondances (il en faut une)");
                    ok = false;
                    break;
                }
                map.Src[i] = found;
                map.Sign[i] = sign;
            }
            map.Detected = ok;
            rapport = ok
                ? $"repère détecté : Unity(x,y,z) = ({Fmt(map, 0)}, {Fmt(map, 1)}, {Fmt(map, 2)})"
                : "repère NON détecté — " + string.Join(" ; ", lines);
            return map;
        }

        static string Fmt(AxisMap m, int i) =>
            (m.Sign[i] < 0 ? "-" : "") + "xyz"[m.Src[i]] + "_blender";
    }
}
