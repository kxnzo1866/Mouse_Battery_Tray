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
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

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
                using (Bitmap sheet = new Bitmap(760, 560))
                {
                    using (Graphics g = Graphics.FromImage(sheet))
                    {
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        for (int row = 0; row < 2; row++)
                        {
                            bool dark = row == 0;
                            using (SolidBrush b = new SolidBrush(dark ? Color.FromArgb(28, 28, 28) : Color.FromArgb(238, 238, 238)))
                                g.FillRectangle(b, 0, row * 130, 760, 130);
                            int x = 10;
                            int[] levels = { 100, 60, 30, 8, 0, 70, 0 };
                            IconMode[] modes = { IconMode.Normal, IconMode.Normal, IconMode.Normal, IconMode.Normal, IconMode.Charging, IconMode.Dim, IconMode.Empty };
                            for (int i = 0; i < levels.Length; i++)
                            {
                                using (Icon ic = Ui.MakeIcon(levels[i], modes[i], 16, dark))
                                using (Bitmap bm = ic.ToBitmap())
                                {
                                    g.DrawImage(bm, new Rectangle(x, row * 130 + 10, 64, 64));
                                    g.DrawImage(bm, new Rectangle(x + 70, row * 130 + 80, 16, 16));
                                }
                                using (Icon ic = Ui.MakeIcon(levels[i], modes[i], 24, dark))
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
                                g.DrawImage(bm, 10 + i * 330, 280);
                            }
                        }
                    }
                    sheet.Save(args[1], ImageFormat.Png);
                }
                return 0;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, "MouseBatteryTray_SingleInstance", out created))
            {
                if (!created) return 0;
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
            return 0;
        }
    }
}
