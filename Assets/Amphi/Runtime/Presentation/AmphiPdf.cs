// Lecture d'un PDF page par page, en texture.
//
// POURQUOI DEUX MOTEURS ET AUCUN PLUGIN. Unity ne sait pas lire un PDF. Mais
// les deux plateformes où tourne l'amphi en ont un, intégré au système :
//
//   - sur le Quest, Android porte `android.graphics.pdf.PdfRenderer` depuis
//     l'API 21. On l'appelle par JNI : rien à installer, rien à licencier ;
//   - dans l'éditeur Windows, `Windows.Data.Pdf` fait le même travail. On
//     l'appelle par un petit script PowerShell, qui rend toutes les pages en
//     PNG dans un cache, une fois pour toutes.
//
// Les deux rendent le même objet : un nombre de pages, le rapport de chaque
// page, et une texture à la demande. Le reste de l'écran ne sait pas lequel
// travaille.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Amphi
{
    public abstract class AmphiPdf : IDisposable
    {
        /// <summary>Nombre de pages, connu dès l'ouverture.</summary>
        public int Pages { get; protected set; }

        /// <summary>Largeur sur hauteur de chaque page (un PDF peut mêler
        /// paysage et portrait).</summary>
        public float[] Rapports { get; protected set; }

        /// <summary>Rend la page <paramref name="i"/> (à partir de 0) sur
        /// <paramref name="largeur"/> pixels, et la passe au rappel. Rend null
        /// en cas d'échec.</summary>
        public abstract IEnumerator Rendre(int i, int largeur, Action<Texture2D> rappel);

        /// <summary>Vrai si les lignes de la texture vont du haut vers le bas
        /// (Android) : l'écran l'affiche alors avec un uvRect inversé.</summary>
        public virtual bool Retournee => false;

        public virtual void Dispose() { }

        /// <summary>Le moteur de la plateforme courante, ou null si elle n'en
        /// a pas. <paramref name="erreur"/> dit pourquoi.</summary>
        public static AmphiPdf Ouvrir(string chemin, out string erreur)
        {
            erreur = null;
            if (!File.Exists(chemin)) { erreur = "fichier introuvable : " + chemin; return null; }
            if (!EstUnPdf(chemin))
            {
                erreur = "ce fichier n'est pas un PDF (le serveur a sans doute renvoyé une page web)";
                return null;
            }
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return new AmphiPdfAndroid(chemin);
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                return new AmphiPdfWindows(chemin);
#else
                erreur = "aucun moteur PDF sur cette plateforme";
                return null;
#endif
            }
            catch (Exception e)
            {
                erreur = e.Message;
                return null;
            }
        }

        /// <summary>Un PDF commence par « %PDF ». Un lien Drive mal partagé
        /// rend une page HTML de connexion : on le dit, au lieu de planter.</summary>
        public static bool EstUnPdf(string chemin)
        {
            try
            {
                using (var f = File.OpenRead(chemin))
                {
                    var t = new byte[4];
                    return f.Read(t, 0, 4) == 4 && t[0] == '%' && t[1] == 'P'
                           && t[2] == 'D' && t[3] == 'F';
                }
            }
            catch { return false; }
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// PdfRenderer d'Android, par JNI.
    /// </summary>
    /// <remarks>
    /// Le bitmap ARGB_8888 d'Android range ses octets en R, G, B, A : c'est
    /// exactement `TextureFormat.RGBA32`, sans conversion. Ses lignes vont en
    /// revanche du haut vers le bas, à l'inverse d'Unity : la texture sort
    /// retournée, et l'écran l'affiche avec un uvRect inversé plutôt que de
    /// retourner des millions de pixels à la main.
    ///
    /// Le fond d'une page PDF est TRANSPARENT pour PdfRenderer : sans
    /// `eraseColor(blanc)`, une page de texte noir s'afficherait sur le fond
    /// sombre de l'écran, c'est-à-dire illisible.
    /// </remarks>
    public class AmphiPdfAndroid : AmphiPdf
    {
        public override bool Retournee => true;

        readonly AndroidJavaObject _pfd, _rendu;

        public AmphiPdfAndroid(string chemin)
        {
            using (var fichier = new AndroidJavaObject("java.io.File", chemin))
            using (var pfdCls = new AndroidJavaClass("android.os.ParcelFileDescriptor"))
            {
                int lecture = pfdCls.GetStatic<int>("MODE_READ_ONLY");
                _pfd = pfdCls.CallStatic<AndroidJavaObject>("open", fichier, lecture);
            }
            _rendu = new AndroidJavaObject("android.graphics.pdf.PdfRenderer", _pfd);
            Pages = _rendu.Call<int>("getPageCount");
            Rapports = new float[Pages];
            for (int i = 0; i < Pages; i++)
                using (var p = _rendu.Call<AndroidJavaObject>("openPage", i))
                {
                    Rapports[i] = p.Call<int>("getWidth") / (float)Mathf.Max(1, p.Call<int>("getHeight"));
                    p.Call("close");
                }
        }

        public override IEnumerator Rendre(int i, int largeur, Action<Texture2D> rappel)
        {
            Texture2D tex = null;
            try
            {
                int hauteur = Mathf.Max(1, Mathf.RoundToInt(largeur / Rapports[i]));
                using (var page = _rendu.Call<AndroidJavaObject>("openPage", i))
                using (var cfg = new AndroidJavaClass("android.graphics.Bitmap$Config"))
                using (var argb = cfg.GetStatic<AndroidJavaObject>("ARGB_8888"))
                using (var bmpCls = new AndroidJavaClass("android.graphics.Bitmap"))
                using (var bmp = bmpCls.CallStatic<AndroidJavaObject>("createBitmap", largeur, hauteur, argb))
                {
                    bmp.Call("eraseColor", -1);                     // 0xFFFFFFFF, blanc opaque
                    // `page.Call("render", bmp, null, null, 1)` NE MARCHE PAS :
                    // Unity déduit la signature JNI du type des arguments, et
                    // un null n'en a pas. On vise donc la méthode par sa
                    // signature exacte. 1 = RENDER_MODE_FOR_DISPLAY.
                    IntPtr cls = AndroidJNI.GetObjectClass(page.GetRawObject());
                    IntPtr mid = AndroidJNI.GetMethodID(cls, "render",
                        "(Landroid/graphics/Bitmap;Landroid/graphics/Rect;Landroid/graphics/Matrix;I)V");
                    AndroidJNI.CallVoidMethod(page.GetRawObject(), mid, new[]
                    {
                        new jvalue { l = bmp.GetRawObject() },
                        new jvalue { l = IntPtr.Zero },
                        new jvalue { l = IntPtr.Zero },
                        new jvalue { i = 1 },
                    });
                    AndroidJNI.DeleteLocalRef(cls);
                    page.Call("close");
                    using (var buf = new AndroidJavaClass("java.nio.ByteBuffer")
                               .CallStatic<AndroidJavaObject>("allocate", largeur * hauteur * 4))
                    {
                        bmp.Call("copyPixelsToBuffer", buf);
                        sbyte[] s = buf.Call<sbyte[]>("array");
                        var octets = new byte[s.Length];
                        Buffer.BlockCopy(s, 0, octets, 0, s.Length);
                        // SetPixelData sur le niveau 0 puis Apply(true) : les
                        // mipmaps sont calculés ici. LoadRawTextureData
                        // exigerait la chaîne complète et refuserait l'image.
                        tex = new Texture2D(largeur, hauteur, TextureFormat.RGBA32, true);
                        tex.SetPixelData(octets, 0);
                        tex.Apply(true, true);
                    }
                    bmp.Call("recycle");
                }
            }
            catch (Exception e) { Debug.LogWarning("[Amphi] page PDF " + i + " : " + e.Message); }
            if (tex != null) Reglages(tex);
            rappel(tex);
            yield break;
        }

        public override void Dispose()
        {
            try { _rendu?.Call("close"); _pfd?.Call("close"); } catch { }
            _rendu?.Dispose(); _pfd?.Dispose();
        }

        internal static void Reglages(Texture2D t)
        {
            t.wrapMode = TextureWrapMode.Clamp;
            // Trilinéaire et anisotrope : l'écran est vu de biais depuis les
            // ailes, et un texte sans mipmap y scintille.
            t.filterMode = FilterMode.Trilinear;
            t.anisoLevel = 8;
        }
    }
#endif

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    /// <summary>
    /// Windows.Data.Pdf, par un script PowerShell qui rend toutes les pages
    /// en PNG dans un cache.
    /// </summary>
    /// <remarks>
    /// Le rendu est fait UNE fois par fichier, dans un processus à part : il
    /// ne bloque pas l'éditeur, et un cache par empreinte du fichier évite de
    /// le refaire à chaque ouverture. Mesuré : 1,4 s pour cinq pages.
    /// </remarks>
    public class AmphiPdfWindows : AmphiPdf
    {
        const int LargeurCache = 2400;
        readonly string _dossier;
        readonly List<string> _fichiers = new List<string>();

        public AmphiPdfWindows(string chemin)
        {
            string empreinte;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var f = File.OpenRead(chemin))
                empreinte = BitConverter.ToString(md5.ComputeHash(f)).Replace("-", "").Substring(0, 16);
            _dossier = Path.Combine(Application.temporaryCachePath, "PdfPages", empreinte);

            if (!Directory.Exists(_dossier) || Directory.GetFiles(_dossier, "page_*.png").Length == 0)
                RendreTout(chemin, _dossier);

            _fichiers.AddRange(Directory.GetFiles(_dossier, "page_*.png"));
            _fichiers.Sort(StringComparer.Ordinal);
            Pages = _fichiers.Count;
            if (Pages == 0) throw new Exception("le moteur PDF de Windows n'a rendu aucune page");
            Rapports = new float[Pages];
            for (int i = 0; i < Pages; i++) Rapports[i] = RapportPng(_fichiers[i]);
        }

        public override IEnumerator Rendre(int i, int largeur, Action<Texture2D> rappel)
        {
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                tex.LoadImage(File.ReadAllBytes(_fichiers[i]), false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Trilinear;
                tex.anisoLevel = 8;
            }
            catch (Exception e) { Debug.LogWarning("[Amphi] page " + i + " : " + e.Message); tex = null; }
            rappel(tex);
            yield break;
        }

        /// <summary>Rapport lu dans l'en-tête IHDR du PNG, sans décoder
        /// l'image.</summary>
        static float RapportPng(string png)
        {
            using (var f = File.OpenRead(png))
            {
                var t = new byte[24];
                f.Read(t, 0, 24);
                int l = (t[16] << 24) | (t[17] << 16) | (t[18] << 8) | t[19];
                int h = (t[20] << 24) | (t[21] << 16) | (t[22] << 8) | t[23];
                return l / (float)Mathf.Max(1, h);
            }
        }

        static void RendreTout(string pdf, string sortie)
        {
            // WinRT REFUSE LES BARRES OBLIQUES. Unity rend ses chemins avec
            // des « / » (temporaryCachePath, dataPath) ; GetFolderFromPathAsync
            // les rejette par une AggregateException sans autre explication.
            // GetFullPath les remet en « \ ».
            pdf = Path.GetFullPath(pdf);
            sortie = Path.GetFullPath(sortie);
            Directory.CreateDirectory(sortie);
            string script = Path.Combine(Application.temporaryCachePath, "amphi_pdf_winrt.ps1");
            File.WriteAllText(script, Script);
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" "
                          + $"-Pdf \"{pdf}\" -Sortie \"{sortie}\" -Largeur {LargeurCache}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using (var p = System.Diagnostics.Process.Start(info))
            {
                string err = p.StandardError.ReadToEnd();
                p.WaitForExit(120000);
                if (p.ExitCode != 0)
                    throw new Exception("rendu PDF Windows en échec : " + err);
            }
        }

        // Windows.Data.Pdf, depuis PowerShell 5.1 : les méthodes WinRT sont
        // asynchrones, on les attend par AsTask().
        const string Script = @"param([string]$Pdf, [string]$Sortie, [int]$Largeur = 2400)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.RandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$m = [System.WindowsRuntimeSystemExtensions].GetMethods()
$op = ($m | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
$ac = ($m | ? { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction' })[0]
function A($o, [Type]$t) { $x = $op.MakeGenericMethod($t).Invoke($null, @($o)); $x.Wait(); $x.Result }
function B($o) { $x = $ac.Invoke($null, @($o)); $x.Wait() }
$f = A ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Pdf)) ([Windows.Storage.StorageFile])
$d = A ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($f)) ([Windows.Data.Pdf.PdfDocument])
$dir = A ([Windows.Storage.StorageFolder]::GetFolderFromPathAsync($Sortie)) ([Windows.Storage.StorageFolder])
for ($i = 0; $i -lt $d.PageCount; $i++) {
  $p = $d.GetPage($i)
  $o = New-Object Windows.Data.Pdf.PdfPageRenderOptions
  $o.DestinationWidth = $Largeur
  $o.DestinationHeight = [uint32]([math]::Round($Largeur * $p.Size.Height / $p.Size.Width))
  $c = A ($dir.CreateFileAsync(('page_{0:D4}.png' -f ($i + 1)), [Windows.Storage.CreationCollisionOption]::ReplaceExisting)) ([Windows.Storage.StorageFile])
  $s = A ($c.OpenAsync([Windows.Storage.FileAccessMode]::ReadWrite)) ([Windows.Storage.Streams.IRandomAccessStream])
  B ($p.RenderToStreamAsync($s, $o))
  $s.Dispose(); $p.Dispose()
}
";
    }
#endif
}
