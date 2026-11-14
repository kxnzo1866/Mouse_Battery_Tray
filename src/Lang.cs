using System;
using System.Collections.Generic;
using System.Globalization;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Translations: every text shown by the app comes from this table.
    // ------------------------------------------------------------------
    static class Lang
    {
        // { key, English, French }. In French,   is a non-breaking space before ":" "?" and "%".
        static readonly string[][] Table = {
            new[] { "App.Name", "Mouse Battery Tray", "Mouse Battery Tray" },
            new[] { "Lang.en", "English", "English" },
            new[] { "Lang.fr", "Français", "Français" },

            // Details window
            new[] { "NoMouse", "No mouse detected", "Aucune souris détectée" },
            new[] { "State.Charged", "Charged (plugged in)", "Chargée (branchée)" },
            new[] { "State.Charging", "Charging", "En charge" },
            new[] { "State.Settling", "Settling after charge", "Stabilisation après charge" },
            new[] { "State.OnBattery", "On battery", "Sur batterie" },
            new[] { "State.Asleep", "Asleep or off", "En veille ou éteinte" },
            new[] { "State.Error", "Read error", "Erreur de lecture" },
            new[] { "State.NotDetected", "Not detected", "Non détectée" },
            new[] { "State.NoMouse", "No mouse found", "Aucune souris trouvée" },
            new[] { "Big.Charged", "Charged", "Chargée" },
            new[] { "Big.Charging", "Charging", "En charge" },
            new[] { "Big.Settling", "Settling…", "Stabilisation…" },
            new[] { "MinLeft", "~{0} min left", "encore ~{0} min" },
            new[] { "LastKnown", "last known value", "dernière valeur connue" },
            new[] { "Row.Status", "Status", "État" },
            new[] { "Row.Voltage", "Voltage", "Tension" },
            new[] { "Row.Connection", "Connection", "Connexion" },
            new[] { "Row.ReportedLevel", "Reported level", "Niveau annoncé" },
            new[] { "Row.Level", "Level", "Niveau" },
            new[] { "Row.LastReading", "Last reading", "Dernière lecture" },
            new[] { "Conn.USB cable", "USB cable", "Câble USB" },
            new[] { "Conn.2.4 GHz wireless receiver", "2.4 GHz wireless receiver", "Récepteur sans fil 2,4 GHz" },
            new[] { "Conn.Bluetooth", "Bluetooth", "Bluetooth" },
            new[] { "Gear.Tip", "Settings", "Paramètres" },

            // Notification area: tooltip and low battery notification
            new[] { "Tip.Charged", "{0}: charged ({1})", "{0} : chargée ({1})" },
            new[] { "Tip.Charging", "{0}: charging", "{0} : en charge" },
            new[] { "Tip.ChargingPct", "{0}: charging ({1})", "{0} : en charge ({1})" },
            new[] { "Tip.Settling", "{0}: settling after charge", "{0} : stabilisation après charge" },
            new[] { "Tip.Level", "{0}: {1}", "{0} : {1}" },
            new[] { "Tip.AsleepLast", "{0}: asleep ({1})", "{0} : en veille ({1})" },
            new[] { "Tip.NotDetectedLast", "{0}: not detected ({1})", "{0} : non détectée ({1})" },
            new[] { "Tip.Asleep", "{0}: asleep or off", "{0} : en veille ou éteinte" },
            new[] { "Tip.NotDetected", "{0}: not detected", "{0} : non détectée" },
            new[] { "Alert.Title", "Mouse battery low", "Batterie de la souris faible" },
            new[] { "Alert.Text", "{0}: {1} left. Time to plug it in.", "{0} : {1} restants. Pense à la brancher." },

            // Right-click menu
            new[] { "Menu.ShowDetails", "Show details", "Afficher les détails" },
            new[] { "Menu.Refresh", "Refresh", "Actualiser" },
            new[] { "Menu.Mouse", "Mouse to display", "Souris à afficher" },
            new[] { "Menu.Automatic", "Automatic", "Automatique" },
            new[] { "Menu.Settings", "Settings…", "Paramètres…" },
            new[] { "Menu.Startup", "Start with Windows", "Démarrer avec Windows" },
            new[] { "Menu.Alert", "Low battery alert ({0})", "Alerte batterie faible ({0})" },
            new[] { "Menu.Exit", "Exit", "Quitter" },

            // Settings window
            new[] { "Set.Title", "Settings - Mouse Battery Tray", "Paramètres - Mouse Battery Tray" },
            new[] { "Set.General", "General", "Général" },
            new[] { "Set.Language", "Language", "Langue" },
            new[] { "Set.Theme", "Theme", "Thème" },
            new[] { "Theme.system", "System", "Système" },
            new[] { "Theme.light", "Light", "Clair" },
            new[] { "Theme.dark", "Dark", "Sombre" },
            new[] { "Set.Startup", "Start with Windows", "Démarrer avec Windows" },
            new[] { "Set.Battery", "Battery", "Batterie" },
            new[] { "Set.Alert", "Low battery alert below", "Alerte batterie faible sous" },
            new[] { "Set.Mouse", "Mouse to display", "Souris à afficher" },
            new[] { "Set.MouseHint", "Available when several mice are detected.", "Disponible quand plusieurs souris sont détectées." },
            new[] { "Set.Interval", "Read the mouse every", "Lire la souris toutes les" },
            new[] { "Interval.Sec", "{0} s", "{0} s" },
            new[] { "Interval.Min", "{0} min", "{0} min" },
            new[] { "Set.RefreshNow", "Refresh now", "Actualiser maintenant" },
            new[] { "Set.Reading", "Reading…", "Lecture en cours…" },
            new[] { "Set.LastReading", "Last reading: {0}", "Dernière lecture : {0}" },
            new[] { "Set.NoReading", "No reading yet", "Aucune lecture pour l'instant" },
            new[] { "Set.LastNext", "Last reading: {0}\nNext around {1}", "Dernière lecture : {0}\nProchaine vers {1}" },
            new[] { "Set.Adaptive", "Read less often while the level does not change", "Lire moins souvent quand le niveau ne bouge pas" },
            new[] { "Set.Diagnostic", "Diagnostic", "Diagnostic" },
            new[] { "Set.Log", "Write a diagnostic log", "Enregistrer un journal de diagnostic" },
            new[] { "Set.LogFolder", "Open the folder", "Ouvrir le dossier" },
            new[] { "Set.LogHint",
                "Useful to report a problem. The log keeps at most {0} files of {1} MB, the oldest are deleted automatically.",
                "Utile pour signaler un problème. Le journal garde au plus {0} fichiers de {1} Mo, les plus anciens sont supprimés automatiquement." },
            new[] { "Set.Updates", "Updates", "Mises à jour" },
            new[] { "Upd.Current", "Current version: {0}", "Version actuelle : {0}" },
            new[] { "Upd.Check", "Check for updates", "Rechercher des mises à jour" },
            new[] { "Upd.Checking", "Checking…", "Recherche en cours…" },
            new[] { "Upd.UpToDate", "The app is up to date.", "L'appli est à jour." },
            new[] { "Upd.Available", "Version {0} is available.", "La version {0} est disponible." },
            new[] { "Upd.Install", "Install", "Installer" },
            new[] { "Upd.Notes", "What's new", "Voir les nouveautés" },
            new[] { "Upd.Downloading", "Downloading… {0}", "Téléchargement… {0}" },
            new[] { "Upd.Confirm",
                "Version {0} will be downloaded from GitHub, then the app will close and the installer will open.\n\nContinue?",
                "La version {0} va être téléchargée depuis GitHub, puis l'appli va se fermer et l'installateur va s'ouvrir.\n\nContinuer ?" },
            new[] { "Upd.Error", "Failed: {0}", "Échec : {0}" },
            new[] { "Upd.NoRelease", "no version has been published on GitHub yet.", "aucune version n'est encore publiée sur GitHub." },
            new[] { "Upd.BadAnswer", "unexpected answer from GitHub.", "réponse inattendue de GitHub." },
            new[] { "Upd.NoInstaller", "this version has no installer attached.", "cette version n'a pas d'installateur joint." },
            new[] { "Upd.BadAddress", "unexpected download address.", "adresse de téléchargement inattendue." },
            new[] { "Upd.TooLarge", "the file is unexpectedly large.", "le fichier est anormalement gros." },
            new[] { "Upd.BadChecksum", "the downloaded file is damaged (checksum mismatch).", "le fichier téléchargé est abîmé (empreinte incorrecte)." },
            new[] { "Set.About", "About", "À propos" },
            new[] { "About.Version", "Version {0}", "Version {0}" },
            new[] { "About.GitHub", "Project page on GitHub", "Page du projet sur GitHub" },
            new[] { "About.License", "MIT License, © 2026 kxnzo1866.", "Licence MIT, © 2026 kxnzo1866." },
            new[] { "About.ViewLicense", "View the license", "Voir la licence" },
            new[] { "About.ChargeTitle", "Why ~95% as soon as the mouse is plugged in?", "Pourquoi ~95 % dès qu'on branche la souris ?" },
            new[] { "About.ChargeText",
                "Some mice (Pulsar, and Logitech mice that only report a voltage) estimate their battery from the voltage. " +
                "When plugged in, the voltage rises and the reported level is overestimated. Once unplugged, it settles back " +
                "within a few minutes, so the app hides the percentage while charging and for 5 min afterwards. " +
                "This comes from the mouse, not from this app.",
                "Certaines souris (Pulsar, et les Logitech qui n'indiquent qu'une tension) estiment leur batterie à partir de la tension. " +
                "Branchée, cette tension monte et le niveau annoncé est surestimé. Une fois débranchée, la valeur redescend " +
                "en quelques minutes : l'appli masque donc le pourcentage pendant la charge et les 5 min qui suivent. " +
                "Cela vient de la souris, pas de cette application." },
            new[] { "About.Unofficial", "Unofficial project, not affiliated with Pulsar or Logitech.", "Projet non officiel, non affilié à Pulsar ni à Logitech." },
            new[] { "Set.Close", "Close", "Fermer" },

            // Error messages
            new[] { "Err.Startup", "Could not change the \"Start with Windows\" setting.\n\n{0}", "Impossible de modifier le démarrage avec Windows.\n\n{0}" },
            new[] { "Err.OpenLink", "Could not open the link.\n\n{0}", "Impossible d'ouvrir le lien.\n\n{0}" },
        };

        static readonly Dictionary<string, string> En = Build(1);
        static readonly Dictionary<string, string> Fr = Build(2);

        static Dictionary<string, string> Build(int column)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            foreach (string[] row in Table) d[row[0]] = row[column];
            return d;
        }

        public static string Code = "en";
        public static event EventHandler Changed;

        public static string Normalize(string code)
        {
            return code == "fr" ? "fr" : code == "en" ? "en" : "";
        }

        // Language used when nothing is saved yet: the Windows display language, English by default.
        public static string FromWindows()
        {
            return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
        }

        public static void Set(string code)
        {
            string c = Normalize(code);
            Code = c.Length > 0 ? c : FromWindows();
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        public static CultureInfo Culture
        {
            get { return CultureInfo.GetCultureInfo(Code == "fr" ? "fr-FR" : "en-US"); }
        }

        public static string T(string key)
        {
            string v;
            if ((Code == "fr" ? Fr : En).TryGetValue(key, out v)) return v;
            return En.TryGetValue(key, out v) ? v : key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(Culture, T(key), args);
        }

        public static string Pct(int value) { return Pct(value.ToString(Culture)); }
        public static string Pct(string value) { return Code == "fr" ? value + " %" : value + "%"; }

        // Connection types are stored in English by the device code; translate them for display.
        public static string Connection(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "—";
            string v;
            return (Code == "fr" ? Fr : En).TryGetValue("Conn." + raw, out v) ? v : raw;
        }

        // Keys present in one language only (used by the self-check).
        public static List<string> MissingKeys()
        {
            List<string> missing = new List<string>();
            foreach (string[] row in Table)
                if (row.Length != 3 || string.IsNullOrEmpty(row[1]) || string.IsNullOrEmpty(row[2])) missing.Add(row[0]);
            return missing;
        }
    }
}
