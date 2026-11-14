using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBatteryTray
{
    class UpdateInfo
    {
        public Version Version;          // null if the release tag is not a version number
        public string Tag = "", PageUrl = "", AssetName = "", AssetUrl = "", Sha256 = "";
        public long Size;
    }

    // ------------------------------------------------------------------
    //  Updates: reads the latest release published on GitHub, downloads its installer
    //  and starts it so it updates the current copy (standard or portable) in place.
    // ------------------------------------------------------------------
    static class Updater
    {
        public const string Repo = "kxnzo1866/Mouse_Battery_Tray";
        const string AppId = "{F3477553-85CB-47BC-8BBF-E7B255F3805A}";   // same AppId as installer\setup.iss
        const long MaxSize = 50L * 1024 * 1024;

        public static Version Current
        {
            get
            {
                Version v = typeof(Updater).Assembly.GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string Format(Version v)
        {
            return v.Major + "." + v.Minor + "." + Math.Max(0, v.Build);
        }

        // "v1.3.0", "1.3", "v1.3.0-beta" -> 1.3.0 ; null if it is not a version.
        public static Version ParseTag(string tag)
        {
            string t = (tag ?? "").Trim().TrimStart('v', 'V');
            int cut = t.IndexOfAny(new char[] { '-', '+', ' ' });
            if (cut >= 0) t = t.Substring(0, cut);
            Version v;
            if (!Version.TryParse(t, out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public static bool IsNewer(UpdateInfo u)
        {
            return u.Version != null && u.Version > Current;
        }

        static HttpWebRequest Request(string url, string accept)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2, required by GitHub
            HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "MouseBatteryTray/" + Format(Current);
            r.Accept = accept;
            r.Timeout = 15000;
            r.ReadWriteTimeout = 30000;
            return r;
        }

        // Latest published release (drafts and pre-releases are ignored by GitHub). Throws on network errors.
        public static UpdateInfo FetchLatest()
        {
            string json;
            try
            {
                using (WebResponse resp = Request("https://api.github.com/repos/" + Repo + "/releases/latest", "application/vnd.github+json").GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream()))
                    json = sr.ReadToEnd();
            }
            catch (WebException e)
            {
                HttpWebResponse r = e.Response as HttpWebResponse;
                if (r != null && r.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException(Lang.T("Upd.NoRelease"));
                throw;
            }

            Dictionary<string, object> rel = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (rel == null) throw new InvalidDataException(Lang.T("Upd.BadAnswer"));
            UpdateInfo u = new UpdateInfo();
            u.Tag = Str(rel, "tag_name");
            u.PageUrl = Str(rel, "html_url");
            u.Version = ParseTag(u.Tag);

            object list;
            object[] assets = rel.TryGetValue("assets", out list) ? list as object[] : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name");
                    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.IndexOf("Setup", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    u.AssetName = name;
                    u.AssetUrl = Str(a, "browser_download_url");
                    long size;
                    if (long.TryParse(Str(a, "size"), out size)) u.Size = size;
                    string digest = Str(a, "digest");
                    if (digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) u.Sha256 = digest.Substring(7).ToLowerInvariant();
                    break;
                }
            }
            return u;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : "";
        }

        // Downloads the installer into %TEMP%\MouseBatteryTray-update and checks its SHA-256 when GitHub provides it.
        public static string Download(UpdateInfo u, Action<int> progress)
        {
            if (u.AssetUrl.Length == 0) throw new InvalidOperationException(Lang.T("Upd.NoInstaller"));
            if (!u.AssetUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Lang.T("Upd.BadAddress"));
            string dir = Path.Combine(Path.GetTempPath(), "MouseBatteryTray-update");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, Path.GetFileName(u.AssetName));

            using (WebResponse resp = Request(u.AssetUrl, "application/octet-stream").GetResponse())
            using (Stream src = resp.GetResponseStream())
            using (FileStream dst = File.Create(path))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : u.Size;
                if (total > MaxSize) throw new InvalidDataException(Lang.T("Upd.TooLarge"));
                byte[] buf = new byte[81920];
                long done = 0;
                int n, last = -1;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    if (done > MaxSize) throw new InvalidDataException(Lang.T("Upd.TooLarge"));
                    if (total > 0 && progress != null)
                    {
                        int pct = (int)Math.Min(100, done * 100 / total);
                        if (pct != last) { last = pct; progress(pct); }
                    }
                }
            }

            if (u.Sha256.Length > 0)
            {
                string actual;
                using (SHA256 sha = SHA256.Create())
                using (FileStream fs = File.OpenRead(path))
                    actual = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (actual != u.Sha256)
                {
                    File.Delete(path);
                    throw new InvalidDataException(Lang.T("Upd.BadChecksum"));
                }
            }
            return path;
        }

        // Command line given to the installer: same language as the app, and the folder of the current copy.
        public static string InstallerArguments()
        {
            string args = "/LANG=" + (Lang.Code == "fr" ? "french" : "english");
            if (AppSettings.IsPortable)
                return args + " /PORTABLE=1 /DIR=\"" + Path.GetDirectoryName(Application.ExecutablePath) + "\"";
            string installed = InstalledDir();
            return installed.Length > 0 ? args + " /DIR=\"" + installed + "\"" : args;
        }

        public static void Launch(string installer)
        {
            Process.Start(new ProcessStartInfo(installer, InstallerArguments()) { UseShellExecute = true });
        }

        // Folder of the standard installation, as recorded by the installer ("" if not installed).
        static string InstalledDir()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppId + "_is1", "InstallLocation", null);
                return v == null ? "" : v.ToString().TrimEnd('\\');
            }
            catch { return ""; }
        }
    }
}
