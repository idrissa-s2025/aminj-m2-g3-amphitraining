// Un siège où le joueur peut se poser.
//
// Le repère de l'objet est posé AU SOL, sous le centre de l'assise, cap vers
// la scène : c'est exactement ce que donne le fichier meta, et donc ce que
// voit un occupant du rendu Blender.
//
// DEUX FAÇONS DE S'ASSEOIR, et la différence compte :
//   - vous êtes physiquement assis sur une chaise : le casque donne déjà la
//     bonne hauteur d'yeux pour une assise ordinaire. On ne corrige que de
//     l'écart entre CETTE assise et la vôtre, soit rien sur un siège de
//     gradin et 0,29 m sur le bureau (`assisVirtuel` faux) ;
//   - vous êtes debout dans votre pièce : sans correction, vous verriez la
//     salle depuis 1,70 m alors que vos voisins sont assis. On ABAISSE alors
//     le rig jusqu'à la hauteur d'yeux d'une personne assise ici
//     (`assisVirtuel` vrai).
// Le bouton A (ou X) bascule entre les deux une fois assis.
//
// LE MODE DEBOUT MONTAIT AU LIEU DE DESCENDRE. Il ajoutait la hauteur
// d'assise à la tête d'un joueur debout : 1,68 + 0,45, des yeux à 2,13 m
// dans un siège de spectateur. La hauteur visée est désormais calculée,
// assise plus buste, et comparée à la tête réelle du joueur.
//
// Le même composant sert au bureau de la scène, où l'on s'assoit sur le
// plateau à 0,74 m : seule `hauteurAssise` change.

using UnityEngine;

namespace Amphi
{
    public class AmphiSiege : MonoBehaviour
    {
        [Tooltip("Identifiant d'origine, par exemple C3_7 ou L-1_5_2.")]
        public string identifiant;

        [Tooltip("Rang, de 0 (près de la scène) au dernier.")]
        public int rang;

        [Tooltip("Hauteur d'assise au-dessus du sol, en mètres : 0,45 pour "
               + "un siège, celle du plateau pour le bureau.")]
        public float hauteurAssise = 0.45f;

        /// <summary>Assise d'une chaise ordinaire, celle où le joueur est
        /// physiquement assis quand il n'est pas debout.</summary>
        public const float AssiseOrdinaire = 0.45f;

        /// <summary>Des fesses aux yeux, adulte assis, en mètres.</summary>
        public const float Buste = 0.78f;

        /// <summary>
        /// De combien monter (ou descendre) le rig pour qu'un joueur dont les
        /// yeux sont à `yeuxJoueur` du sol de sa pièce voie comme une
        /// personne assise ici.
        /// </summary>
        public float Correction(float yeuxJoueur)
        {
            return assisVirtuel
                ? hauteurAssise + Buste - yeuxJoueur
                : hauteurAssise - AssiseOrdinaire;
        }

        [Tooltip("Vrai si le joueur est debout dans sa pièce et qu'il faut "
               + "simuler la position assise en remontant le point de vue.")]
        public bool assisVirtuel = false;
    }
}
