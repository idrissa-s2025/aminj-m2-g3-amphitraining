// Post-traitement : bloom, tonemapping, exposition.
//
// POURQUOI IL N'Y EN AVAIT PAS. La scène était construite sans aucun Volume.
// Dans URP, le post-traitement n'existe que si TROIS choses sont réunies : un
// profil de volume qui porte les effets, un objet Volume global qui l'applique,
// et une caméra dont `renderPostProcessing` est vrai. Il en manquait trois sur
// trois. Aucune erreur n'est levée dans ce cas — il ne se passe simplement
// rien, ce qui est la façon la plus coûteuse de ne pas marcher.
//
// POURQUOI PAR REFLEXION. `Bloom`, `Tonemapping` et
// `UniversalAdditionalCameraData` vivent dans les assemblées d'URP. Les
// nommer en dur lie la compilation de TOUT le dossier à la présence du
// paquet : greffé dans un projet en pipeline intégré, plus rien ne compile,
// et l'erreur ne parle ni d'URP ni de post-traitement. Par réflexion, leur
// absence ne coûte qu'une ligne de journal.
//
// LE BLOOM EST UN EFFET COUTEUX, ET SUR CASQUE IL SE PAIE DEUX FOIS — une par
// œil. Les valeurs posées ici sont volontairement sobres : un seuil haut, une
// intensité basse. Il ne s'agit pas de faire briller la salle, mais de rendre
// les luminaires et les écrans aux bonnes intensités, car une source lumineuse
// sans halo se lit comme un rectangle blanc collé au plafond.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AmphiEditor
{
    public static class AmphiPostTraitement
    {
        const string DossierRendu = "Assets/Amphi/Rendering";
        const string CheminProfil = DossierRendu + "/PostTraitement_Amphi.asset";
        const string NomVolume = "POST_TRAITEMENT";

        [MenuItem("AmphiTraining/6. Post-traitement (bloom, tonemapping)", false, 26)]
        public static void Poser()
        {
            if (Appliquer(out string message)) Debug.Log("[Amphi] " + message);
            else Debug.LogWarning("[Amphi] " + message);
            var s = SceneManager.GetActiveScene();
            if (s.IsValid()) EditorSceneManager.MarkSceneDirty(s);
        }

        /// <summary>
        /// Pose le profil, le volume global et active le post-traitement sur la
        /// caméra du rig. Renvoie false si URP n'est pas là.
        /// </summary>
        public static bool Appliquer(out string message)
        {
            var tProfil = Trouver("UnityEngine.Rendering.VolumeProfile");
            var tVolume = Trouver("UnityEngine.Rendering.Volume");
            if (tProfil == null || tVolume == null)
            {
                message = "pas de système de Volume dans ce projet : "
                          + "post-traitement impossible (pipeline intégré ?)";
                return false;
            }

            var profil = AssetDatabase.LoadAssetAtPath<ScriptableObject>(CheminProfil);
            if (profil == null)
            {
                profil = ScriptableObject.CreateInstance(tProfil);
                Directory.CreateDirectory(DossierRendu);
                AssetDatabase.CreateAsset(profil, CheminProfil);
            }

            // Les entrées nulles d'un profil écrit par une version fautive.
            //
            // Tant que les composants n'étaient pas attachés au fichier, le
            // profil se rechargeait avec trois `{fileID: 0}`. Les laisser en
            // place ferait échouer `Has` et empilerait des doublons à chaque
            // reconstruction.
            PurgerNuls(profil, tProfil);

            int poses = 0;
            poses += Bloom(profil, tProfil) ? 1 : 0;
            poses += Tonemapping(profil, tProfil) ? 1 : 0;
            poses += Couleurs(profil, tProfil) ? 1 : 0;
            EditorUtility.SetDirty(profil);

            // Le volume GLOBAL : il s'applique partout, sans collisionneur de
            // délimitation. Une salle unique n'a pas besoin de volumes locaux.
            var go = GameObject.Find(NomVolume) ?? new GameObject(NomVolume);
            var vol = go.GetComponent(tVolume) ?? go.AddComponent(tVolume);
            Ecrire(vol, "isGlobal", true);
            Ecrire(vol, "priority", 0f);
            Ecrire(vol, "weight", 1f);
            Ecrire(vol, "sharedProfile", profil);
            Ecrire(vol, "profile", profil);

            int cams = ActiverSurCameras();
            AssetDatabase.SaveAssets();

            // ON RELIT CE QU'ON VIENT D'ECRIRE.
            //
            // Compter les appels réussis ne prouve rien : c'est ainsi que ce
            // fichier a pu annoncer « 3 effets » sur un profil enregistré
            // vide. On recharge donc l'asset depuis le disque et on vérifie
            // que chaque effet y est, actif, avec au moins un paramètre dont
            // `overrideState` est coché — sans quoi il est présent et inerte.
            AssetDatabase.ImportAsset(CheminProfil);
            var relu = AssetDatabase.LoadAssetAtPath<ScriptableObject>(CheminProfil);
            var manquants = new List<string>();
            var inertes = new List<string>();
            foreach (var nom in new[] { "Bloom", "Tonemapping", "ColorAdjustments" })
            {
                var t = Trouver("UnityEngine.Rendering.Universal." + nom);
                if (t == null) { manquants.Add(nom + " (type absent)"); continue; }
                object c = null;
                var champ = tProfil.GetField("components");
                if (champ != null && champ.GetValue(relu) is System.Collections.IEnumerable l)
                    foreach (var x in l)
                        if (x != null && t.IsInstanceOfType(x)) { c = x; break; }
                if (c == null) { manquants.Add(nom); continue; }
                if (!AuMoinsUnOverride(c)) inertes.Add(nom);
            }

            if (manquants.Count > 0 || inertes.Count > 0)
            {
                message = "post-traitement INCOMPLET après relecture du profil"
                    + (manquants.Count > 0
                       ? " — absents : " + string.Join(", ", manquants) : "")
                    + (inertes.Count > 0
                       ? " — présents mais sans aucun paramètre coché : "
                         + string.Join(", ", inertes) : "")
                    + $". Profil : {CheminProfil}";
                return false;
            }

            message = $"post-traitement posé et RELU : {poses} effets "
                      + "(Bloom, Tonemapping, ColorAdjustments), paramètres cochés, "
                      + $"{cams} caméra(s).";
            return true;
        }

        /// <summary>Vrai si au moins un paramètre du composant est réellement
        /// appliqué, c'est-à-dire coché.</summary>
        static bool AuMoinsUnOverride(object composant)
        {
            foreach (var f in composant.GetType().GetFields(
                         BindingFlags.Public | BindingFlags.Instance))
            {
                var p = f.GetValue(composant);
                if (p == null) continue;
                var ov = p.GetType().GetProperty("overrideState")
                         ?? (MemberInfo)p.GetType().GetField("overrideState");
                object v = ov is PropertyInfo pi ? pi.GetValue(p)
                         : ov is FieldInfo fi ? fi.GetValue(p) : null;
                if (v is bool b && b) return true;
            }
            return false;
        }

        /// <summary>Retire de la liste les composants perdus (`fileID: 0`).</summary>
        static void PurgerNuls(ScriptableObject profil, Type tProfil)
        {
            var champ = tProfil.GetField("components");
            if (champ == null) return;
            if (!(champ.GetValue(profil) is System.Collections.IList liste)) return;
            int retires = 0;
            for (int i = liste.Count - 1; i >= 0; i--)
            {
                var x = liste[i];
                if (x == null || (x is UnityEngine.Object uo && uo == null))
                { liste.RemoveAt(i); retires++; }
            }
            if (retires > 0)
                Debug.Log($"[Amphi] {retires} effet(s) perdu(s) retiré(s) du profil : "
                          + "ils avaient été ajoutés sans être écrits dans l'asset.");
        }

        // ------------------------------------------------------------ effets
        static bool Bloom(ScriptableObject profil, Type tProfil)
        {
            var t = Trouver("UnityEngine.Rendering.Universal.Bloom");
            if (t == null) return false;
            var c = Composant(profil, tProfil, t);
            if (c == null) return false;
            // Seuil HAUT : seul ce qui est vraiment une source déborde. Un
            // seuil bas fait baver les murs clairs, et une salle dont les murs
            // bavent paraît embuée, pas lumineuse.
            Parametre(c, "threshold", 1.15f);
            Parametre(c, "intensity", 0.28f);
            Parametre(c, "scatter", 0.62f);
            // Le mode « haute qualité » double le coût pour un gain invisible
            // à 72 Hz sur casque.
            Parametre(c, "highQualityFiltering", false);
            Parametre(c, "downscale", 1);        // moitié de résolution
            Parametre(c, "maxIterations", 4);
            return true;
        }

        static bool Tonemapping(ScriptableObject profil, Type tProfil)
        {
            var t = Trouver("UnityEngine.Rendering.Universal.Tonemapping");
            if (t == null) return false;
            var c = Composant(profil, tProfil, t);
            if (c == null) return false;
            // Neutral, et non ACES. ACES écrase les hautes lumières et
            // désature : sur une salle claire aux tablettes blanches, il
            // grise tout ce qu'on vient de relever. Neutral conserve les
            // teintes et se contente de replier ce qui dépasse.
            Parametre(c, "mode", 1);             // 0 None, 1 Neutral, 2 ACES
            return true;
        }

        static bool Couleurs(ScriptableObject profil, Type tProfil)
        {
            var t = Trouver("UnityEngine.Rendering.Universal.ColorAdjustments");
            if (t == null) return false;
            var c = Composant(profil, tProfil, t);
            if (c == null) return false;
            // 0,15 porté à 0,40 IL le 2026-09-23 (« ajoute plus de lumière ») :
            // un quart de diaphragme de plus, pris après la cuisson, donc sur
            // toute la salle à la fois, sans toucher aux rapports.
            Parametre(c, "postExposure", 0.40f);
            Parametre(c, "contrast", 6f);
            Parametre(c, "saturation", 4f);
            return true;
        }

        // ------------------------------------------------------------ caméra
        static int ActiverSurCameras()
        {
            var t = Trouver("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (t == null) return 0;
            int n = 0;
            var rig = GameObject.Find("RIG_VR");
            if (rig == null) return 0;
            foreach (var cam in rig.GetComponentsInChildren<Camera>(true))
            {
                var data = cam.gameObject.GetComponent(t) ?? cam.gameObject.AddComponent(t);
                Ecrire(data, "renderPostProcessing", true);
                // L'antialiasing de post-traitement (FXAA/SMAA) fait double
                // emploi avec le MSAA 4x du casque, et coûte une passe de
                // plus. On le laisse à « None ».
                Ecrire(data, "antialiasing", 0);
                // Le bloom a besoin de valeurs au-dessus de 1 pour distinguer
                // une source d'un mur blanc : sans HDR, tout est écrêté à 1 et
                // l'effet se réduit à un flou des zones claires.
                cam.allowHDR = true;
                EditorUtility.SetDirty(cam);
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------ outils
        static Type Trouver(string nom)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(nom, false);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>Le composant de ce type dans le profil, ajouté au besoin.</summary>
        static object Composant(ScriptableObject profil, Type tProfil, Type tEffet)
        {
            try
            {
                var has = tProfil.GetMethod("Has", new[] { typeof(Type) });
                var tryGet = tProfil.GetMethod("TryGet", new[] { typeof(Type), typeof(object).MakeByRefType() });
                var add = tProfil.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
                if (add == null) return null;
                if (has != null && (bool)has.Invoke(profil, new object[] { tEffet }))
                {
                    // déjà présent : on le retrouve dans la liste des composants
                    var champ = tProfil.GetField("components");
                    if (champ != null && champ.GetValue(profil) is System.Collections.IEnumerable liste)
                        foreach (var x in liste)
                            if (x != null && tEffet.IsInstanceOfType(x)) return x;
                    return null;
                }
                var neuf = add.Invoke(profil, new object[] { tEffet, true });

                // ET ON L'ATTACHE AU FICHIER.
                //
                // `VolumeProfile.Add` instancie le composant et l'ajoute à la
                // liste, mais il ne l'écrit PAS dans l'asset : c'est
                // l'inspecteur d'Unity qui appelle AddObjectToAsset de son
                // côté. Sans cet appel, le composant n'existe qu'en mémoire,
                // et à la sérialisation la liste garde trois entrées dont les
                // références retombent à `{fileID: 0}`.
                //
                // Le profil paraissait donc correct — trois lignes, trois
                // effets annoncés — et arrivait VIDE dans l'APK. C'est la
                // panne la plus coûteuse de ce fichier, parce qu'elle ne
                // produit ni erreur ni avertissement.
                if (neuf is UnityEngine.Object obj && AssetDatabase.Contains(profil)
                    && !AssetDatabase.Contains(obj))
                {
                    obj.name = tEffet.Name;
                    obj.hideFlags = HideFlags.HideInHierarchy;
                    AssetDatabase.AddObjectToAsset(obj, profil);
                }
                return neuf;
            }
            catch (Exception e)
            {
                Debug.Log($"[Amphi] effet {tEffet.Name} non posé : {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Règle un paramètre de volume : `.value` ET `.overrideState`.
        /// </summary>
        /// <remarks>
        /// Oublier `overrideState` est l'erreur classique : la valeur est bien
        /// écrite, le profil l'affiche, et elle n'est jamais appliquée parce
        /// que la case n'est pas cochée. On perd une heure à régler un effet
        /// qui n'est pas branché.
        /// </remarks>
        static void Parametre(object composant, string nom, object valeur)
        {
            var champ = composant.GetType().GetField(nom,
                BindingFlags.Public | BindingFlags.Instance);
            if (champ == null) return;
            var p = champ.GetValue(composant);
            if (p == null) return;
            var tv = p.GetType().GetField("m_Value",
                BindingFlags.NonPublic | BindingFlags.Instance)
                ?? p.GetType().GetField("value", BindingFlags.Public | BindingFlags.Instance);
            var ov = p.GetType().GetProperty("overrideState")
                ?? (MemberInfo)p.GetType().GetField("overrideState");
            try
            {
                if (tv != null)
                {
                    object v = valeur;
                    var cible = tv.FieldType;
                    if (cible.IsEnum) v = Enum.ToObject(cible, Convert.ToInt32(valeur));
                    else if (cible != valeur.GetType()) v = Convert.ChangeType(valeur, cible);
                    tv.SetValue(p, v);
                }
                if (ov is PropertyInfo pi) pi.SetValue(p, true);
                else if (ov is FieldInfo fi) fi.SetValue(p, true);
            }
            catch (Exception e)
            {
                Debug.Log($"[Amphi] paramètre « {nom} » non réglé : {e.Message}");
            }
        }

        static void Ecrire(object cible, string nom, object valeur)
        {
            if (cible == null) return;
            var t = cible.GetType();
            var pi = t.GetProperty(nom, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null && pi.CanWrite)
            {
                try { pi.SetValue(cible, valeur); return; } catch { }
            }
            var fi = t.GetField(nom, BindingFlags.Public | BindingFlags.Instance);
            if (fi != null)
            {
                try { fi.SetValue(cible, valeur); } catch { }
            }
        }
    }
}
