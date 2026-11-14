using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Optional diagnostic log (off by default). Same content as the --once mode, written at every reading,
    //  plus the app events. Size-limited: MouseBatteryTray.log rotates into .1.log and .2.log (3 x 1 MB at most).
    //  Standard mode: %LOCALAPPDATA%\MouseBatteryTray\logs. Portable mode: "logs" folder next to the exe.
    // ------------------------------------------------------------------
    static class Logger
    {
        public const long MaxBytes = 1024 * 1024;
        public const int Files = 3;
        static readonly object sync = new object();
        public static bool Enabled;

        public static string Folder
        {
            get
            {
                if (AppSettings.IsPortable) return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "logs");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseBatteryTray", "logs");
            }
        }

        public static string FilePath { get { return Path.Combine(Folder, "MouseBatteryTray.log"); } }

        static string Rotated(int i) { return Path.Combine(Folder, "MouseBatteryTray." + i + ".log"); }

        public static void Write(string text)
        {
            if (!Enabled || string.IsNullOrEmpty(text)) return;
            string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            StringBuilder sb = new StringBuilder();
            string[] lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
                sb.Append(i == 0 ? stamp + "  " : new string(' ', stamp.Length + 2)).Append(lines[i]).Append("\r\n");
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    Rotate();
                    File.AppendAllText(FilePath, sb.ToString(), Encoding.UTF8);
                }
                catch { }   // logging must never break the app
            }
        }

        // MouseBatteryTray.log -> .1.log -> .2.log -> deleted.
        static void Rotate()
        {
            FileInfo current = new FileInfo(FilePath);
            if (!current.Exists || current.Length < MaxBytes) return;
            string oldest = Rotated(Files - 1);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = Files - 2; i >= 1; i--)
                if (File.Exists(Rotated(i))) File.Move(Rotated(i), Rotated(i + 1));
            File.Move(FilePath, Rotated(1));
        }
    }
}
