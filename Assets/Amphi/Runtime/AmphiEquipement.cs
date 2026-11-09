// Marque un équipement de la salle — écran, micro, pupitre, projecteurs.
//
// POURQUOI UN COMPOSANT PLUTOT QU'UN NOM. Chercher « l'écran » par son nom
// marche jusqu'à la première renommée, et casse en silence : `Find` rend
// null, le script continue, et l'écran reste noir sans qu'aucune erreur ne
// soit levée. Un composant se cherche par son type, survit aux renommages, et
// porte en plus le GENRE de l'objet — ce qu'il EST, et non ce qu'il
// s'appelle. C'est ce genre qui permet d'écrire « allume tous les écrans »
// sans énumérer leurs noms.
//
// Les équipements restent STATIQUES par défaut : ce sont des ouvrages fixes,
// et leur éclairage est cuit comme le reste. Pour en rendre un saisissable,
// il faut lui retirer ses drapeaux statiques dans l'inspecteur — sinon sa
// carte de lumière le suit sans bouger et il paraît éteint dès qu'on le
// déplace.

using UnityEngine;

namespace Amphi
{
    public class AmphiEquipement : MonoBehaviour
    {
        [Tooltip("Nom de famille : Ecran_Principal, Micro, Pupitre…")]
        public string nom;

        [Tooltip("Genre : ecran, micro, mobilier, projecteur, luminaire")]
        public string genre;

        [Tooltip("Encombrement mesuré dans Blender, en mètres")]
        public Vector3 taille;

        /// <summary>Tous les équipements d'un genre donné.</summary>
        public static AmphiEquipement[] DuGenre(string genre)
        {
            var groupe = GameObject.Find("AMPHI/EQUIPEMENTS");
            if (groupe == null) return new AmphiEquipement[0];
            var tous = groupe.GetComponentsInChildren<AmphiEquipement>(true);
            int n = 0;
            foreach (var e in tous) if (e.genre == genre) n++;
            var res = new AmphiEquipement[n];
            n = 0;
            foreach (var e in tous) if (e.genre == genre) res[n++] = e;
            return res;
        }
    }
}
