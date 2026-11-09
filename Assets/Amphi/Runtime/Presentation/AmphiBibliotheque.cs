// Les documents que l'écran de présentation sait ouvrir, et d'où ils viennent.
//
// DEUX SOURCES, SANS CLAVIER A TAPER DANS LE CASQUE.
//
//   1. Le casque. Un dossier `Presentations` dans l'espace de l'application :
//        Quest 3 > Android/data/com.amphitraining.vr/files/Presentations
//      On y range ses PDF, dans un sous-dossier par catégorie si l'on veut :
//        Cours/, Conference/, Presentation/
//      Un PDF posé à la racine va dans « Présentation ».
//
//   2. En ligne. Un fichier `liens.txt` dans le même dossier, une ligne par
//      document :
//        Cours | Signalisation, module 2 | https://drive.google.com/file/d/…/view
//      Le lien d'un Google Slides, d'un PDF sur Google Drive, sur GitHub ou
//      sur Dropbox est converti tout seul en lien de téléchargement. Le
//      document est téléchargé au premier clic, puis gardé dans le casque.
//
// Taper une adresse au clavier reste possible dans l'éditeur. Dans le casque,
// sans le SDK de Meta, il n'y a pas de clavier système fiable : le fichier de
// liens est la voie sûre, et il se prépare une fois sur un PC.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace Amphi
{
    public class AmphiDocument
    {
        public string titre;
        public string categorie;      // Cours, Conférence, Présentation
        public string fichier;        // chemin local du PDF, null tant qu'il n'est pas téléchargé
        public string url;            // null pour un document du casque
        public long octets;
        public DateTime date;
        public int pages;             // 0 tant qu'on ne l'a pas ouvert

        public bool EnLigne => !string.IsNullOrEmpty(url);
        public bool Disponible => !string.IsNullOrEmpty(fichier) && File.Exists(fichier);
    }

    public class AmphiBibliotheque
    {
        public static readonly string[] Categories = { "Cours", "Conférence", "Présentation" };

        public readonly List<AmphiDocument> documents = new List<AmphiDocument>();

        public static string Dossier =>
            Path.Combine(Application.persistentDataPath, "Presentations");
        static string DossierTelechargements => Path.Combine(Dossier, "_telecharges");
        public static string FichierLiens => Path.Combine(Dossier, "liens.txt");

        /// <summary>Chemin affiché à l'écran, lisible par un humain.</summary>
        public static string CheminLisible =>
#if UNITY_ANDROID && !UNITY_EDITOR
            "Quest 3 › Android › data › " + Application.identifier + " › files › Presentations";
#else
            Dossier;
#endif

        // --------------------------------------------------------- inventaire
        /// <summary>Relit le dossier et le fichier de liens.</summary>
        public void Actualiser()
        {
            documents.Clear();
            Directory.CreateDirectory(Dossier);
            foreach (var c in Categories)
                Directory.CreateDirectory(Path.Combine(Dossier, SansAccent(c)));
            if (!File.Exists(FichierLiens)) File.WriteAllText(FichierLiens, ModeleLiens);

            foreach (var pdf in Directory.GetFiles(Dossier, "*.pdf", SearchOption.AllDirectories))
            {
                if (pdf.Contains(Path.DirectorySeparatorChar + "_telecharges")) continue;
                var info = new FileInfo(pdf);
                string sous = Path.GetFileName(Path.GetDirectoryName(pdf));
                documents.Add(new AmphiDocument
                {
                    titre = Titre(Path.GetFileNameWithoutExtension(pdf)),
                    categorie = Categorie(sous),
                    fichier = pdf,
                    octets = info.Length,
                    date = info.LastWriteTime,
                });
            }

            foreach (var ligne in File.ReadAllLines(FichierLiens))
            {
                var l = ligne.Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                var parts = l.Split('|').Select(p => p.Trim()).ToArray();
                string url = parts[parts.Length - 1];
                if (!url.StartsWith("http")) continue;
                string cat = parts.Length >= 3 ? Categorie(parts[0]) : "Présentation";
                string titre = parts.Length >= 2 ? parts[parts.Length - 2] : NomDepuisUrl(url);
                var doc = new AmphiDocument { titre = titre, categorie = cat, url = url };
                string local = CheminTelecharge(url);
                if (File.Exists(local))
                {
                    doc.fichier = local;
                    var info = new FileInfo(local);
                    doc.octets = info.Length;
                    doc.date = info.LastWriteTime;
                }
                documents.Add(doc);
            }
        }

        public IEnumerable<AmphiDocument> DeLaCategorie(string c) =>
            documents.Where(d => d.categorie == c)
                     .OrderByDescending(d => d.date).ThenBy(d => d.titre);

        public IEnumerable<AmphiDocument> DuCasque =>
            documents.Where(d => !d.EnLigne).OrderByDescending(d => d.date);

        public IEnumerable<AmphiDocument> EnLigneListe =>
            documents.Where(d => d.EnLigne);

        // ------------------------------------------------------ téléchargement
        /// <summary>
        /// Télécharge le document, rapporte l'avancement, et ne garde le
        /// fichier que s'il commence bien par « %PDF ».
        /// </summary>
        public IEnumerator Telecharger(AmphiDocument doc, Action<float, long> avance,
                                       Action<string> fin)
        {
            Directory.CreateDirectory(DossierTelechargements);
            string cible = CheminTelecharge(doc.url);
            string provisoire = cible + ".part";
            string lien = LienDirect(doc.url);
            using (var req = UnityWebRequest.Get(lien))
            {
                req.downloadHandler = new DownloadHandlerFile(provisoire) { removeFileOnAbort = true };
                req.timeout = 120;
                var op = req.SendWebRequest();
                while (!op.isDone)
                {
                    avance?.Invoke(req.downloadProgress, (long)req.downloadedBytes);
                    yield return null;
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    fin?.Invoke(Message(req));
                    yield break;
                }
            }
            if (!AmphiPdf.EstUnPdf(provisoire))
            {
                File.Delete(provisoire);
                fin?.Invoke("Ce lien ne mène pas à un PDF. Le serveur a renvoyé une page web : "
                            + "sur Google Drive, ouvrez le partage à « tous les utilisateurs "
                            + "disposant du lien », puis réessayez.");
                yield break;
            }
            if (File.Exists(cible)) File.Delete(cible);
            File.Move(provisoire, cible);
            doc.fichier = cible;
            var info = new FileInfo(cible);
            doc.octets = info.Length;
            doc.date = info.LastWriteTime;
            fin?.Invoke(null);
        }

        /// <summary>Ajoute un lien tapé à la main au fichier de liens, pour
        /// qu'il reste après la séance.</summary>
        public AmphiDocument AjouterLien(string url, string categorie)
        {
            url = url.Trim();
            var existant = documents.FirstOrDefault(d => d.url == url);
            if (existant != null) return existant;
            var doc = new AmphiDocument { titre = NomDepuisUrl(url), categorie = categorie, url = url };
            File.AppendAllText(FichierLiens,
                $"{Environment.NewLine}{categorie} | {doc.titre} | {url}");
            documents.Add(doc);
            return doc;
        }

        static string Message(UnityWebRequest r)
        {
            if (r.result == UnityWebRequest.Result.ConnectionError)
                return "Pas de connexion. Vérifiez le Wi-Fi du casque, puis réessayez.";
            if (r.responseCode == 404) return "Adresse introuvable (erreur 404). Vérifiez le lien.";
            if (r.responseCode == 401 || r.responseCode == 403)
                return "Accès refusé. Le document doit être partagé à « tous les utilisateurs "
                       + "disposant du lien ».";
            return "Téléchargement impossible : " + r.error;
        }

        // --------------------------------------------------------------- liens
        /// <summary>
        /// Convertit un lien de partage en lien de téléchargement direct.
        /// </summary>
        /// <remarks>
        /// Google Slides a un export PDF natif : c'est la voie la plus simple
        /// pour un PowerPoint, qu'on ouvre dans Slides et qu'on partage.
        /// </remarks>
        public static string LienDirect(string url)
        {
            Match m;
            m = Regex.Match(url, @"docs\.google\.com/presentation/d/([\w-]+)");
            if (m.Success) return $"https://docs.google.com/presentation/d/{m.Groups[1].Value}/export/pdf";
            m = Regex.Match(url, @"docs\.google\.com/document/d/([\w-]+)");
            if (m.Success) return $"https://docs.google.com/document/d/{m.Groups[1].Value}/export?format=pdf";
            m = Regex.Match(url, @"drive\.google\.com/file/d/([\w-]+)");
            if (m.Success) return $"https://drive.google.com/uc?export=download&id={m.Groups[1].Value}";
            m = Regex.Match(url, @"drive\.google\.com/open\?id=([\w-]+)");
            if (m.Success) return $"https://drive.google.com/uc?export=download&id={m.Groups[1].Value}";
            m = Regex.Match(url, @"github\.com/([^/]+)/([^/]+)/blob/(.+)");
            if (m.Success)
                return $"https://raw.githubusercontent.com/{m.Groups[1].Value}/{m.Groups[2].Value}/{m.Groups[3].Value}";
            if (url.Contains("dropbox.com"))
                return Regex.Replace(url, @"([?&])dl=0", "$1dl=1") + (url.Contains("dl=") ? "" : (url.Contains("?") ? "&dl=1" : "?dl=1"));
            return url;
        }

        static string CheminTelecharge(string url)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
            {
                var h = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url.Trim()));
                return Path.Combine(DossierTelechargements,
                    BitConverter.ToString(h).Replace("-", "").Substring(0, 16) + ".pdf");
            }
        }

        static string NomDepuisUrl(string url)
        {
            if (url.Contains("docs.google.com/presentation")) return "Présentation Google Slides";
            if (url.Contains("drive.google.com")) return "Document Google Drive";
            string nom = Uri.UnescapeDataString(url.Split('?')[0].TrimEnd('/').Split('/').Last());
            return Titre(Path.GetFileNameWithoutExtension(nom));
        }

        /// <summary>« securite-ferroviaire_module-2 » devient « Securite
        /// ferroviaire module 2 ».</summary>
        public static string Titre(string nom)
        {
            var t = Regex.Replace(nom, @"[_\-]+", " ").Trim();
            return t.Length == 0 ? "Sans titre" : char.ToUpper(t[0]) + t.Substring(1);
        }

        public static string Categorie(string brut)
        {
            string s = SansAccent(brut ?? "").ToLowerInvariant();
            if (s.StartsWith("cours")) return "Cours";
            if (s.StartsWith("conf")) return "Conférence";
            return "Présentation";
        }

        static string SansAccent(string s) =>
            s.Replace("é", "e").Replace("É", "E").Replace("è", "e");

        public static string Taille(long o) =>
            o >= 1 << 20 ? (o / 1048576f).ToString("0.0").Replace('.', ',') + " Mo"
                         : Mathf.Max(1, o / 1024) + " Ko";

        const string ModeleLiens =
@"# Documents en ligne de l'écran de présentation, un par ligne :
#
#   Catégorie | Titre | Adresse
#
# Catégorie : Cours, Conférence ou Présentation.
# Adresse : un Google Slides (exporté en PDF tout seul), un PDF sur Google
# Drive partagé « à tous les utilisateurs disposant du lien », un PDF sur
# GitHub (lien du fichier) ou sur Dropbox.
#
# Exemple :
# Cours | Signalisation, module 2 | https://drive.google.com/file/d/IDENTIFIANT/view
";
    }
}
