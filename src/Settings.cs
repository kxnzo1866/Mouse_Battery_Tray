using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Settings: HKCU\Software\MouseBatteryTray in standard mode,
    //  portable.ini next to the exe in portable mode (nothing in the registry then).
    // ------------------------------------------------------------------
    static class AppSettings
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MouseBatteryTray";
        const string SettingsKey = @"Software\MouseBatteryTray";

        public static readonly int[] Intervals = { 30, 60, 120, 300, 600 };   // seconds

        // Portable mode: a portable.ini file sits next to the exe.
        public static string PortableFile
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "portable.ini"); }
        }

        public static bool IsPortable { get { return File.Exists(PortableFile); } }

        public static string Get(string name, string def)
        {
            try
            {
                if (IsPortable)
                {
                    foreach (string line in File.ReadAllLines(PortableFile))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), name, StringComparison.OrdinalIgnoreCase))
                            return line.Substring(eq + 1).Trim();
                    }
                    return def;
                }
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\" + SettingsKey, name, null);
                return v == null ? def : v.ToString();
            }
            catch { return def; }
        }

        public static void Set(string name, string value)
        {
            try
            {
                if (IsPortable)
                {
                    List<string> lines = new List<string>(File.ReadAllLines(PortableFile));
                    bool found = false;
                    for (int i = 0; i < lines.Count; i++)
                    {
                        int eq = lines[i].IndexOf('=');
                        if (eq > 0 && string.Equals(lines[i].Substring(0, eq).Trim(), name, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = name + "=" + value;
                            found = true;
                        }
                    }
                    if (!found) lines.Add(name + "=" + value);
                    File.WriteAllLines(PortableFile, lines.ToArray());
                }
                else Registry.SetValue(@"HKEY_CURRENT_USER\" + SettingsKey, name, value, RegistryValueKind.String);
            }
            catch { }
        }

        static int GetInt(string name, int def, int min, int max)
        {
            int v;
            if (!int.TryParse(Get(name, ""), out v)) return def;
            return Math.Max(min, Math.Min(max, v));
        }

        // "fr", "en", or "" when nothing is saved (the installer writes it; otherwise the Windows language is used).
        public static string Language
        {
            get { return Lang.Normalize(Get("Language", "")); }
            set { Set("Language", Lang.Normalize(value)); }
        }

        public static string Theme
        {
            get { return MouseBatteryTray.Theme.Normalize(Get("Theme", "system")); }
            set { Set("Theme", MouseBatteryTray.Theme.Normalize(value)); }
        }

        public static string SelectedDevice
        {
            get { return Get("SelectedDevice", ""); }
            set { Set("SelectedDevice", value); }
        }

        public static bool AlertEnabled
        {
            get { return Get("LowBatteryAlert", "1") != "0"; }
            set { Set("LowBatteryAlert", value ? "1" : "0"); }
        }

        public const int AlertMin = 5, AlertMax = 50, AlertDefault = 15;

        public static int AlertLevel
        {
            get { return GetInt("AlertLevel", AlertDefault, AlertMin, AlertMax); }
            set { Set("AlertLevel", Math.Max(AlertMin, Math.Min(AlertMax, value)).ToString()); }
        }

        public static int IntervalSeconds
        {
            get
            {
                int v = GetInt("RefreshInterval", 60, Intervals[0], Intervals[Intervals.Length - 1]);
                foreach (int i in Intervals) if (i >= v) return i;
                return 60;
            }
            set { Set("RefreshInterval", value.ToString()); }
        }

        // ---- Start with Windows ----
        // Standard mode: a value under HKCU\...\Run (also written by the installer).
        // Portable mode: a shortcut in the user's Startup folder, so the registry is never touched.
        static string StartupShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Mouse Battery Tray.lnk"); }
        }

        public static bool StartupEnabled()
        {
            try
            {
                if (IsPortable) return File.Exists(StartupShortcut);
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(RunName) != null;
            }
            catch { return false; }
        }

        // Throws on failure, so the caller can tell the user.
        public static void SetStartup(bool on)
        {
            if (IsPortable)
            {
                if (!on) { if (File.Exists(StartupShortcut)) File.Delete(StartupShortcut); return; }
                CreateShortcut(StartupShortcut, Application.ExecutablePath);
                return;
            }
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                else if (k.GetValue(RunName) != null) k.DeleteValue(RunName, false);
            }
        }

        static void CreateShortcut(string lnkPath, string target)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                try
                {
                    Type lt = lnk.GetType();
                    lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                    lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                    lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
                }
                finally { Marshal.FinalReleaseComObject(lnk); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
    }
}
