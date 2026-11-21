using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace MouseBatteryTray
{
    static class Program
    {
        public const string MutexName = "MouseBatteryTray_SingleInstance";     // also used by installer\setup.iss
        public const string ShowEventName = "MouseBatteryTray_ShowSettings";

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")]
        static extern bool AllowSetForegroundWindow(int processId);

        [STAThread]
        static int Main(string[] args)
        {
            // Diagnostic mode: MouseBatteryTray.exe --once [file] writes a detailed reading, then exits.
            if (args.Length > 0 && args[0] == "--once")
            {
                StringBuilder log = new StringBuilder();
                List<Reading> list = Devices.ReadAll(log);
                log.AppendLine();
                log.AppendLine(list.Count + " mouse/mice found");
                foreach (Reading r in list)
                    log.AppendLine("Result: " + r.Name + " | " + r.State + " | " + r.Percent + "% | charging=" + r.Charging +
                                   " | full=" + r.Full + " | " + r.Millivolts + " mV | " + r.Connection + (r.VoltageBased ? " | voltage-based" : ""));
                if (args.Length > 1) File.WriteAllText(args[1], log.ToString(), Encoding.UTF8);
                else Console.Write(log.ToString());
                return list.Count > 0 ? 0 : 1;
            }

            // Preview mode: MouseBatteryTray.exe --preview file.png draws the icons and the window into an image.
            if (args.Length > 1 && args[0] == "--preview")
            {
                using (Bitmap sheet = new Bitmap(760, 820))
                {
                    using (Graphics g = Graphics.FromImage(sheet))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        // Rows 0-1: battery icon (dark / light taskbar); rows 2-3: "percentage on the icon" option.
                        for (int row = 0; row < 4; row++)
                        {
                            bool dark = row % 2 == 0;
                            bool number = row >= 2;
                            using (SolidBrush b = new SolidBrush(dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(238, 238, 238)))
                                g.FillRectangle(b, 0, row * 130, 760, 130);
                            int x = 10;
                            int[] levels = { 100, 60, 30, 8, 0, 70, 0 };
                            IconMode[] modes = { IconMode.Normal, IconMode.Normal, IconMode.Normal, IconMode.Normal, IconMode.Charging, IconMode.Dim, IconMode.Empty };
                            for (int i = 0; i < levels.Length; i++)
                            {
                                using (Icon ic = Ui.MakeIcon(levels[i], modes[i], 16, dark, number))
                                using (Bitmap bm = ic.ToBitmap())
                                {
                                    g.DrawImage(bm, new Rectangle(x, row * 130 + 10, 64, 64));
                                    g.DrawImage(bm, new Rectangle(x + 70, row * 130 + 80, 16, 16));
                                }
                                using (Icon ic = Ui.MakeIcon(levels[i], modes[i], 24, dark, number))
                                using (Bitmap bm = ic.ToBitmap())
                                    g.DrawImage(bm, new Rectangle(x + 10, row * 130 + 80, 24, 24));
                                x += 105;
                            }
                        }
                        Reading[] states = new Reading[2];
                        states[0] = new Reading(); states[0].State = MouseState.Ok; states[0].Percent = 95; states[0].Charging = true; states[0].Millivolts = 4059;
                        states[0].Brand = "Pulsar"; states[0].Name = "Pulsar X2 V2 Mini"; states[0].Connection = "USB cable"; states[0].VoltageBased = true;
                        states[1] = new Reading(); states[1].State = MouseState.Ok; states[1].Percent = 72;
                        states[1].Brand = "Logitech"; states[1].Name = "Logitech PRO X SUPERLIGHT"; states[1].Connection = "2.4 GHz wireless receiver";
                        for (int i = 0; i < 2; i++)
                        {
                            using (DetailsForm f = new DetailsForm())
                            using (Bitmap bm = new Bitmap(310, 262))
                            {
                                f.ClientSize = new Size(310, 262);
                                f.Current = states[i];
                                DeviceTrack t = new DeviceTrack();
                                t.LastOk = states[i];
                                t.LastTrusted = states[i];
                                f.Track = t;
                                using (Graphics fg = Graphics.FromImage(bm)) f.PaintTo(fg);
                                g.DrawImage(bm, 10 + i * 330, 540);
                            }
                        }
                    }
                    sheet.Save(args[1], ImageFormat.Png);
                }
                string stem = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), Path.GetFileNameWithoutExtension(args[1]));
                PreviewStyles(stem + "-styles.png");
                PreviewSettings(stem + "-settings.png");
                return 0;
            }

            // Single instance. The installer also looks for this mutex to know whether the app is still running.
            bool created;
            using (Mutex mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // Already running: ask the running copy to show its settings window, then quit.
                    try
                    {
                        AllowSetForegroundWindow(-1);   // let the running copy bring its window to the front
                        using (EventWaitHandle show = EventWaitHandle.OpenExisting(ShowEventName)) show.Set();
                    }
                    catch { }
                    return 0;
                }
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
            return 0;
        }

        // Preview of every visual style (light and dark): opaque, then with the simulated Acrylic.
        static void PreviewStyles(string file)
        {
            Reading r = new Reading();
            r.State = MouseState.Ok; r.Percent = 72; r.Millivolts = 3920;
            r.Name = "Logitech PRO X SUPERLIGHT"; r.Connection = "2.4 GHz wireless receiver";
            DeviceTrack t = new DeviceTrack();
            t.LastOk = r;
            t.LastTrusted = r;
            int cw = 330, ch = 257, cols = Theme.Styles.Length;
            using (Bitmap sheet = new Bitmap(cols * cw + 10, 4 * ch + 10, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(sheet))
                {
                    Rectangle all = new Rectangle(0, 0, sheet.Width, sheet.Height);
                    using (Bitmap wall = new Bitmap(sheet.Width, sheet.Height, PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics wg = Graphics.FromImage(wall)) Ui.DrawWallpaper(wg, all);
                        using (Bitmap blur = Ui.Blur(wall, 10))
                        {
                            g.DrawImageUnscaled(wall, 0, 0);
                            for (int row = 0; row < 4; row++)
                                for (int i = 0; i < cols; i++)
                                {
                                    bool dark = row % 2 == 0;
                                    Palette p = Theme.For(Theme.Styles[i], dark);
                                    RectangleF dest = new RectangleF(10 + i * cw, 10 + row * ch, 310, 237);
                                    DetailsForm.PaintPreview(g, dest, p, row >= 2 && p.Translucent, r, t, blur, all);
                                }
                        }
                    }
                }
                sheet.Save(file, ImageFormat.Png);
            }
        }

        // Settings window, both tabs, in every style (light then dark).
        static void PreviewSettings(string file)
        {
            List<Bitmap> shots = new List<Bitmap>();
            try
            {
                foreach (bool dark in new bool[] { false, true })
                    foreach (string style in Theme.Styles)
                    {
                        Theme.Mode = dark ? "dark" : "light";
                        Theme.SetStyle(style, true);
                        using (SettingsForm f = new SettingsForm(new PreviewHost()))
                        {
                            // Child controls are only drawn once the form has been shown: show it off-screen.
                            f.StartPosition = FormStartPosition.Manual;
                            f.Location = new Point(-30000, -30000);
                            f.ShowInTaskbar = false;
                            f.Show();
                            Application.DoEvents();
                            for (int tab = 0; tab < 2; tab++)
                            {
                                f.ShowTab(tab);
                                Application.DoEvents();
                                Bitmap bm = new Bitmap(f.ClientSize.Width, f.ClientSize.Height);
                                f.DrawToBitmap(bm, new Rectangle(Point.Empty, bm.Size));
                                shots.Add(bm);
                            }
                        }
                    }
                int h = 0;
                foreach (Bitmap b in shots) h = Math.Max(h, b.Height);
                // 6 styles per line (two tabs each), light on the first line, dark on the second.
                int lineW = 0;
                for (int i = 0; i < Theme.Styles.Length * 2; i++) lineW += shots[i].Width + 10;
                using (Bitmap sheet = new Bitmap(lineW, 2 * h + 10))
                {
                    using (Graphics g = Graphics.FromImage(sheet))
                    {
                        g.Clear(Color.Gray);
                        for (int line = 0; line < 2; line++)
                        {
                            int x = 0;
                            for (int i = 0; i < Theme.Styles.Length * 2; i++)
                            {
                                Bitmap b = shots[line * Theme.Styles.Length * 2 + i];
                                g.DrawImageUnscaled(b, x, line * (h + 10));
                                x += b.Width + 10;
                            }
                        }
                    }
                    sheet.Save(file, ImageFormat.Png);
                }
            }
            finally
            {
                foreach (Bitmap b in shots) b.Dispose();
            }
        }

        // Stand-in for the running app, for the preview of the settings window.
        class PreviewHost : IAppHost
        {
            readonly List<DeviceTrack> tracks = new List<DeviceTrack>();
            public List<DeviceTrack> Tracks { get { return tracks; } }
            public string SelectedKey { get { return ""; } }
            public DateTime LastRefresh { get { return DateTime.Now; } }
            public DateTime NextRefresh { get { return DateTime.MinValue; } }
            public bool Busy { get { return false; } }
            public void SelectDevice(string key) { }
            public void SetLanguage(string code) { }
            public void SetTheme(string mode) { }
            public void SetVisualStyle(string style) { }
            public void SetEffects(bool on) { }
            public bool SetStartup(bool on, out string error) { error = null; return true; }
            public void SetAlert(bool enabled, int level) { }
            public void SetInterval(int seconds) { }
            public void SetAdaptive(bool on) { }
            public void SetIconPercent(bool on) { }
            public void SetColorLevels(int green, int yellow) { }
            public void SetLogging(bool on) { }
            public void RefreshNow() { }
            public void Exit() { }
            public event EventHandler StateChanged { add { } remove { } }
        }
    }
}
