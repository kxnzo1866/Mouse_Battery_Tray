// Pulsar Battery Tray - shows the battery level of a Pulsar mouse (X2 V2 Mini) in the Windows notification area.
// Protocol taken from the open-source project python-pulsar-mouse-tool (andrewrabert, MIT license).
// Build: see build.ps1 (uses the csc.exe that ships with Windows, no dependencies).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[assembly: System.Reflection.AssemblyTitle("Pulsar Battery Tray")]
[assembly: System.Reflection.AssemblyProduct("Pulsar Battery Tray")]
[assembly: System.Reflection.AssemblyDescription("Battery level of a Pulsar mouse in the notification area (unofficial project)")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace PulsarBattery
{
    enum MouseState { NoDevice, Asleep, Ok, Error }
    enum IconMode { Normal, Charging, Dim, Empty }

    class Reading
    {
        public MouseState State = MouseState.NoDevice;
        public int Percent;
        public bool Charging;
        public int Millivolts;
        public int ProductId;
        public string Error;
        public DateTime Time = DateTime.Now;
    }

    // ------------------------------------------------------------------
    //  HID communication with the mouse / receiver
    // ------------------------------------------------------------------
    static class Device
    {
        const string VendorTag = "vid_3554";      // Pulsar
        const int PidWired = 0xF507;              // X2 V2 Mini plugged in over USB
        const byte ReportId = 0x08;
        const byte CmdStatus = 0x03;
        const byte CmdPower = 0x04;
        const int PacketLen = 17;

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")]
        static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")]
        static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("hid.dll", SetLastError = true)]
        static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] buf, int len);

        [DllImport("setupapi.dll", SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
        static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int required, IntPtr devInfoData);
        [DllImport("setupapi.dll")]
        static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
        static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr template);

        static List<string> FindPaths()
        {
            List<string> list = new List<string>();
            Guid g;
            HidD_GetHidGuid(out g);
            IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
            if (set == new IntPtr(-1)) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    SP_DEVICE_INTERFACE_DATA d = new SP_DEVICE_INTERFACE_DATA();
                    d.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                    int req;
                    SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
                    if (req <= 0) continue;
                    IntPtr buf = Marshal.AllocHGlobal(req);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero))
                        {
                            string path = Marshal.PtrToStringUni(IntPtr.Add(buf, 4));
                            if (path != null && path.ToLowerInvariant().Contains(VendorTag)) list.Add(path);
                        }
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        static int ParsePid(string path)
        {
            string p = path.ToLowerInvariant();
            int i = p.IndexOf("pid_");
            if (i < 0 || i + 8 > p.Length) return 0;
            int pid;
            return int.TryParse(p.Substring(i + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out pid) ? pid : 0;
        }

        static byte[] Build(byte cmd)
        {
            byte[] p = new byte[PacketLen];
            p[0] = ReportId;
            p[1] = cmd;
            int sum = 0;
            for (int i = 0; i < 16; i++) sum += p[i];
            p[16] = (byte)(0x55 - sum);
            return p;
        }

        static string Hex(byte[] b)
        {
            return BitConverter.ToString(b).Replace("-", " ");
        }

        // Sends a command and waits for the matching reply. "dead" = a read is still pending,
        // the stream must not be reused.
        static byte[] Transact(SafeFileHandle h, FileStream fs, byte cmd, int inLen, StringBuilder log, out bool dead)
        {
            dead = false;
            byte[] req = Build(cmd);
            if (!HidD_SetOutputReport(h, req, req.Length))
            {
                int err = Marshal.GetLastWin32Error();
                try { fs.Write(req, 0, req.Length); fs.Flush(); }
                catch (Exception e)
                {
                    if (log != null) log.AppendLine("    send failed (SetOutputReport err " + err + ", Write: " + e.Message + ")");
                    return null;
                }
            }
            if (log != null) log.AppendLine("    -> " + Hex(req));
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(1000);
            while (true)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left <= 0) return null;
                byte[] buf = new byte[inLen];
                IAsyncResult ar = fs.BeginRead(buf, 0, inLen, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(left))
                {
                    dead = true;
                    if (log != null) log.AppendLine("    <- (no reply)");
                    return null;
                }
                int n = fs.EndRead(ar);
                if (log != null) log.AppendLine("    <- " + Hex(buf) + " (" + n + " bytes)");
                if (n >= 10 && buf[0] == ReportId && buf[1] == cmd) return buf;
            }
        }

        public static Reading Read(StringBuilder log)
        {
            Reading best = new Reading();
            try
            {
                List<string> paths = FindPaths();
                if (log != null) log.AppendLine(paths.Count + " Pulsar HID interface(s) found");
                foreach (string path in paths)
                {
                    int pid = ParsePid(path);
                    if (log != null) log.AppendLine("  " + path);
                    using (SafeFileHandle h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero))
                    {
                        if (h.IsInvalid)
                        {
                            if (log != null) log.AppendLine("    open denied (err " + Marshal.GetLastWin32Error() + ")");
                            continue;
                        }
                        IntPtr pp;
                        if (!HidD_GetPreparsedData(h, out pp)) continue;
                        HIDP_CAPS caps;
                        HidP_GetCaps(pp, out caps);
                        HidD_FreePreparsedData(pp);
                        if (log != null)
                            log.AppendLine("    usage " + caps.UsagePage.ToString("X4") + ":" + caps.Usage.ToString("X4") +
                                           " in=" + caps.InputReportByteLength + " out=" + caps.OutputReportByteLength);
                        if (caps.InputReportByteLength != PacketLen || caps.OutputReportByteLength != PacketLen) continue;

                        using (FileStream fs = new FileStream(h, FileAccess.ReadWrite, PacketLen, true))
                        {
                            bool dead;
                            byte[] st = Transact(h, fs, CmdStatus, PacketLen, log, out dead);
                            if (st == null) continue;
                            if (best.State == MouseState.NoDevice)
                            {
                                best.State = MouseState.Asleep;
                                best.ProductId = pid;
                            }
                            if (st[6] == 0) continue;   // receiver present but mouse off / asleep

                            byte[] pw = Transact(h, fs, CmdPower, PacketLen, log, out dead);
                            if (pw == null || pw[6] > 100) continue;
                            Reading r = new Reading();
                            r.State = MouseState.Ok;
                            r.Percent = pw[6];
                            r.Charging = pw[7] != 0;
                            r.Millivolts = (pw[8] << 8) | pw[9];
                            r.ProductId = pid;
                            // If the mouse is also plugged in over USB, prefer that reading.
                            if (best.State != MouseState.Ok || pid == PidWired) best = r;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (best.State != MouseState.Ok)
                {
                    best.State = MouseState.Error;
                    best.Error = e.Message;
                }
                if (log != null) log.AppendLine("Exception: " + e);
            }
            best.Time = DateTime.Now;
            return best;
        }

        public static string ConnectionName(int pid)
        {
            if (pid == PidWired) return "USB cable";
            if (pid == 0) return "—";
            return "2.4 GHz wireless receiver";
        }
    }

    // ------------------------------------------------------------------
    //  Appearance
    // ------------------------------------------------------------------
    static class Ui
    {
        public const string MouseName = "Pulsar X2 V2 Mini";

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        public static bool IsDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return v is int && (int)v == 0;
            }
            catch { return false; }
        }

        public static Color LevelColor(int percent, bool charging)
        {
            if (charging) return Color.FromArgb(31, 111, 235);
            if (percent >= 50) return Color.FromArgb(40, 160, 70);
            if (percent >= 20) return Color.FromArgb(214, 134, 0);
            return Color.FromArgb(214, 48, 49);
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            if (d <= 0 || d > r.Width || d > r.Height) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static bool IsTaskbarDark()
        {
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                return !(v is int) || (int)v == 0;
            }
            catch { return true; }
        }

        public static readonly Color ChargeBlue = Color.FromArgb(31, 111, 235);

        // Lightning bolt fitted inside the given rectangle.
        public static PointF[] Bolt(RectangleF r)
        {
            float[] u = { 0.62f, 0f, 0.16f, 0.57f, 0.46f, 0.57f, 0.36f, 1f, 0.84f, 0.40f, 0.54f, 0.40f };
            PointF[] pts = new PointF[6];
            for (int i = 0; i < 6; i++) pts[i] = new PointF(r.X + u[2 * i] * r.Width, r.Y + u[2 * i + 1] * r.Height);
            return pts;
        }

        // Notification area icon: a battery whose fill follows the level.
        public static Icon MakeIcon(int percent, IconMode mode, int size, bool darkTaskbar)
        {
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;   // crisp lines: x.5 = pixel centre
                    g.Clear(Color.Transparent);
                    float u = size / 16f;
                    float pw = Math.Max(1f, (float)Math.Round(1.2f * u));
                    Color line = darkTaskbar ? Color.White : Color.FromArgb(30, 30, 30);
                    if (mode == IconMode.Empty) line = Color.FromArgb(140, line);

                    RectangleF body = new RectangleF(pw / 2f, 3.5f * u, 13f * u, 9f * u);
                    using (Pen pen = new Pen(line, pw))
                    using (GraphicsPath path = RoundRect(body, 2f * u))
                        g.DrawPath(pen, path);
                    using (SolidBrush nb = new SolidBrush(line))
                    using (GraphicsPath nub = RoundRect(new RectangleF(body.Right + pw / 2f, 6f * u, 1.7f * u, 4f * u), 0.6f * u))
                        g.FillPath(nb, nub);

                    float inset = pw / 2f + u;
                    RectangleF inner = new RectangleF(body.X + inset, body.Y + inset, body.Width - 2 * inset, body.Height - 2 * inset);
                    if (mode == IconMode.Charging)
                    {
                        using (SolidBrush b = new SolidBrush(ChargeBlue)) g.FillRectangle(b, inner);
                        RectangleF br = new RectangleF(body.X + body.Width / 2f - 2.6f * u, body.Y + 0.6f * u, 5.2f * u, body.Height - 1.2f * u);
                        g.FillPolygon(Brushes.White, Bolt(br));
                    }
                    else if (mode != IconMode.Empty && percent > 0)
                    {
                        Color c = mode == IconMode.Dim ? Color.FromArgb(140, 140, 140) : LevelColor(percent, false);
                        float fw = Math.Max(1.5f * u, inner.Width * Math.Min(100, percent) / 100f);
                        using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, inner.X, inner.Y, fw, inner.Height);
                    }
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (Icon tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
                }
                finally { DestroyIcon(h); }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Details window (left click on the icon)
    // ------------------------------------------------------------------
    class DetailsForm : Form
    {
        public Reading Current = new Reading();
        public Reading LastOk;        // last successful reading (time, voltage)
        public Reading LastTrusted;   // last trustworthy percentage (not charging, not settling)
        public DateTime SettleUntil = DateTime.MinValue;
        public DateTime HiddenAt = DateTime.MinValue;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out RECT r);

        public DetailsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            // Small discreet "i" in the top-right corner: explains why the level is off while charging.
            info.AutoSize = false;
            info.Cursor = Cursors.Help;
            info.Paint += delegate(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Color c = Ui.IsDark() ? Color.FromArgb(150, 150, 150) : Color.FromArgb(110, 110, 110);
                using (Pen p = new Pen(c, Math.Max(1f, g.DpiX / 96f)))
                    g.DrawEllipse(p, 1.5f, 1.5f, info.Width - 4f, info.Height - 4f);
                using (SolidBrush b = new SolidBrush(c))
                using (Font f = new Font("Segoe UI Semibold", 7.5f))
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("i", f, b, new RectangleF(0.5f, 0, info.Width, info.Height), sf);
                }
            };
            Controls.Add(info);
            // Clicking the "i" replaces the window content with the explanation; another click closes it.
            info.Click += delegate { showInfo = !showInfo; Relayout(); };
        }

        const string InfoTitle = "Why ~95% as soon as it's plugged in?";
        const string InfoText =
            "This comes from the Pulsar mouse, not from this app.\n\n" +
            "The mouse estimates its battery from the voltage. When plugged in, the voltage rises " +
            "and the reported level is overestimated.\n\n" +
            "Once unplugged, it settles back to the real level within a few minutes. " +
            "So the percentage is hidden while charging and for 5 min afterwards.";

        readonly Label info = new Label();
        bool showInfo;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (showInfo) { showInfo = false; Relayout(); }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HiddenAt = DateTime.UtcNow;
            Hide();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { HiddenAt = DateTime.UtcNow; Hide(); }
            base.OnKeyDown(e);
        }

        public void ShowNearTray()
        {
            showInfo = false;
            Relayout();
            Show();
            Activate();
            Invalidate();
        }

        // Size and position: the window has one extra row while charging (reported level).
        public void Relayout()
        {
            float s;
            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            bool tall = showInfo || (Current.State == MouseState.Ok && Current.Charging);
            Size = new Size((int)(310 * s), (int)((tall ? 262 : 237) * s));
            int isz = (int)(18 * s);
            info.SetBounds(Width - (int)(20 * s) - isz, (int)(14 * s), isz, isz);
            info.BackColor = Ui.IsDark() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
            Screen screen = Visible ? Screen.FromControl(this) : Screen.FromPoint(Cursor.Position);
            Rectangle wa = screen.WorkingArea;
            int right = wa.Right, bottom = wa.Bottom;

            // The working area does not always exclude the taskbar (auto-hide, etc.):
            // read its real size so the window never ends up underneath it.
            IntPtr bar = FindWindow("Shell_TrayWnd", null);
            RECT tr;
            if (bar != IntPtr.Zero && GetWindowRect(bar, out tr))
            {
                Rectangle b = screen.Bounds;
                int tw = tr.Right - tr.Left, th = tr.Bottom - tr.Top;
                bool sameScreen = tr.Left < b.Right && tr.Right > b.Left;
                if (sameScreen && tw >= th && tr.Top + th / 2 > b.Top + b.Height / 2)
                    bottom = Math.Min(bottom, b.Bottom - th);          // taskbar at the bottom
                else if (sameScreen && th > tw && tr.Left + tw / 2 > b.Left + b.Width / 2)
                    right = Math.Min(right, b.Right - tw);             // taskbar on the right
            }

            int m = (int)(12 * s);
            Location = new Point(right - Width - m, bottom - Height - m);
            Invalidate();
        }

        public void showInfoPreview() { showInfo = true; }
        public void PaintTo(Graphics g) { OnPaint(new PaintEventArgs(g, ClientRectangle)); }

        string StateText(Reading r)
        {
            switch (r.State)
            {
                case MouseState.Ok:
                    if (r.Charging) return r.Percent >= 100 ? "Charged (plugged in)" : "Charging";
                    if (r.Time < SettleUntil) return "Settling after charge";
                    return "On battery";
                case MouseState.Asleep: return "Asleep or off";
                case MouseState.Error: return "Read error";
                default: return "Receiver not found";
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            bool dark = Ui.IsDark();
            Color bg = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249);
            Color fg = dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(26, 26, 26);
            Color sub = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
            Color track = dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(222, 222, 222);
            Color border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);

            g.Clear(bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (Pen p = new Pen(border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            bool ok = Current.State == MouseState.Ok;
            bool charging = ok && Current.Charging;
            bool settling = ok && !Current.Charging && Current.Time < SettleUntil;
            bool live = ok && !charging && !settling;          // percentage is trustworthy and current
            Reading shown = live ? Current : LastTrusted;       // source of the displayed percentage
            Reading volt = ok ? Current : LastOk;
            float pad = 20 * s;
            float w = ClientSize.Width - 2 * pad;

            using (SolidBrush bFg = new SolidBrush(fg))
            using (SolidBrush bSub = new SolidBrush(sub))
            using (Font fTitle = new Font("Segoe UI Semibold", 10.5f))
            using (Font fBig = new Font("Segoe UI Light", 32f))
            using (Font fRow = new Font("Segoe UI", 9.5f))
            using (StringFormat right = new StringFormat())
            {
                right.Alignment = StringAlignment.Far;
                right.FormatFlags |= StringFormatFlags.NoWrap;

                g.DrawString(Ui.MouseName, fTitle, bFg, pad - 2 * s, 14 * s);

                if (showInfo)
                {
                    using (Font fHead = new Font("Segoe UI Semibold", 9.5f))
                    using (Font fBody = new Font("Segoe UI", 9f))
                    {
                        RectangleF head = new RectangleF(pad - 2 * s, 46 * s, w + 4 * s, 40 * s);
                        g.DrawString(InfoTitle, fHead, bFg, head);
                        float hh = g.MeasureString(InfoTitle, fHead, (int)head.Width).Height;
                        g.DrawString(InfoText, fBody, bSub, new RectangleF(pad - 2 * s, 46 * s + hh + 8 * s, w + 4 * s, ClientSize.Height - 60 * s - hh));
                    }
                    return;
                }

                RectangleF bar = new RectangleF(pad, 100 * s, w, 8 * s);
                using (GraphicsPath pt = Ui.RoundRect(bar, 4 * s))
                using (SolidBrush bt = new SolidBrush(track))
                    g.FillPath(bt, pt);

                if (charging)
                {
                    // Charging: the percentage sent by the mouse is overestimated, show a bolt instead.
                    // When it reports 100% while plugged in, treat it as fully charged.
                    bool full = Current.Percent >= 100;
                    using (SolidBrush bb = new SolidBrush(full ? Ui.LevelColor(100, false) : Ui.ChargeBlue))
                    {
                        g.FillPolygon(bb, Ui.Bolt(new RectangleF(pad, 44 * s, 24 * s, 40 * s)));
                        using (GraphicsPath pf = Ui.RoundRect(bar, 4 * s)) g.FillPath(bb, pf);
                    }
                    using (Font fMid = new Font("Segoe UI Light", 22f))
                        g.DrawString(full ? "Charged" : "Charging", fMid, bFg, pad + 28 * s, 44 * s);
                }
                else if (settling)
                {
                    using (Font fMid = new Font("Segoe UI Light", 22f))
                        g.DrawString("Settling…", fMid, bSub, pad - 6 * s, 44 * s);
                    int min = Math.Max(1, (int)Math.Ceiling((SettleUntil - DateTime.Now).TotalMinutes));
                    g.DrawString("~" + min + " min left", fRow, bSub, new RectangleF(pad, 64 * s, w, 20 * s), right);
                }
                else
                {
                    string big = shown != null ? shown.Percent + "%" : "—%";
                    g.DrawString(big, fBig, live ? bFg : bSub, pad - 8 * s, 34 * s);
                    string side = live ? StateText(Current) : (shown != null ? "last known value" : "");
                    g.DrawString(side, fRow, bSub, new RectangleF(pad, 64 * s, w, 20 * s), right);
                    if (shown != null && shown.Percent > 0)
                    {
                        float fw = Math.Max(8 * s, w * shown.Percent / 100f);
                        Color c = live ? Ui.LevelColor(shown.Percent, false) : sub;
                        using (GraphicsPath pf = Ui.RoundRect(new RectangleF(pad, 100 * s, fw, 8 * s), 4 * s))
                        using (SolidBrush bf = new SolidBrush(c))
                            g.FillPath(bf, pf);
                    }
                }

                string[][] rows = new string[][] {
                    new string[] { "Status", StateText(Current) },
                    new string[] { "Voltage", volt != null && volt.Millivolts > 0 ? (volt.Millivolts / 1000.0).ToString("0.00") + " V" : "—" },
                    new string[] { "Connection", Device.ConnectionName(Current.State == MouseState.NoDevice ? 0 : Current.ProductId) },
                    // Row shown only while charging: level reported by the mouse (approximate).
                    new string[] { charging ? "Reported level" : null, (Current.Percent >= 100 ? "" : "≈ ") + Current.Percent + "%" },
                    new string[] { "Last reading", LastOk != null ? LastOk.Time.ToString("HH:mm:ss") : "—" },
                };
                float y = 124 * s;
                foreach (string[] row in rows)
                {
                    if (row[0] == null) continue;
                    g.DrawString(row[0], fRow, bSub, pad - 2 * s, y);
                    g.DrawString(row[1], fRow, bFg, new RectangleF(pad, y, w + 2 * s, 22 * s), right);
                    y += 25 * s;
                }
            }
        }
    }

    // ------------------------------------------------------------------
    //  Notification area icon
    // ------------------------------------------------------------------
    class TrayContext : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "PulsarBatteryTray";
        const string SettingsKey = @"Software\PulsarBatteryTray";
        const int LowLevel = 15;

        readonly NotifyIcon tray = new NotifyIcon();
        readonly DetailsForm details = new DetailsForm();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly TaskScheduler ui;
        readonly ToolStripMenuItem itemHeader, itemStartup, itemAlert;

        bool busy;
        bool lowAlerted;
        const int SettleMinutes = 5;
        Reading lastOk, lastTrusted;
        bool wasCharging;
        DateTime settleUntil = DateTime.MinValue;
        Icon currentIcon;

        public TrayContext()
        {
            if (SynchronizationContext.Current == null)
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            ui = TaskScheduler.FromCurrentSynchronizationContext();

            ContextMenuStrip menu = new ContextMenuStrip();
            itemHeader = new ToolStripMenuItem(Ui.MouseName);
            itemHeader.Enabled = false;
            itemStartup = new ToolStripMenuItem("Start with Windows");
            itemStartup.Click += delegate { ToggleStartup(); };
            itemAlert = new ToolStripMenuItem("Low battery alert (" + LowLevel + "%)");
            itemAlert.Click += delegate { SetAlertEnabled(!AlertEnabled()); };
            menu.Items.Add(itemHeader);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Show details", null, delegate { ShowDetails(); });
            menu.Items.Add("Refresh", null, delegate { RefreshNow(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itemStartup);
            menu.Items.Add(itemAlert);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, delegate { ExitThread(); });
            menu.Opening += delegate
            {
                itemStartup.Checked = StartupEnabled();
                itemAlert.Checked = AlertEnabled();
            };

            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (details.Visible) { details.Hide(); return; }
                // Clicking the icon first closes the window (focus loss): do not reopen it right away.
                if ((DateTime.UtcNow - details.HiddenAt).TotalMilliseconds < 300) return;
                ShowDetails();
            };
            Apply(new Reading());
            tray.Visible = true;

            timer.Interval = 60000;
            timer.Tick += delegate { RefreshNow(); };
            timer.Start();
            RefreshNow();
        }

        void ShowDetails()
        {
            details.ShowNearTray();
            RefreshNow();
        }

        void RefreshNow()
        {
            if (busy) return;
            busy = true;
            Task.Factory.StartNew<Reading>(delegate { return Device.Read(null); })
                .ContinueWith(delegate(Task<Reading> t)
                {
                    busy = false;
                    Reading r;
                    if (t.IsFaulted)
                    {
                        r = new Reading();
                        r.State = MouseState.Error;
                        r.Error = t.Exception.GetBaseException().Message;
                    }
                    else r = t.Result;
                    Apply(r);
                }, ui);
        }

        void Apply(Reading r)
        {
            // While charging, the mouse overestimates its level; after unplugging, the value takes a few
            // minutes to settle. The percentage is not shown during these two phases.
            bool settling = false;
            if (r.State == MouseState.Ok)
            {
                lastOk = r;
                if (r.Charging) wasCharging = true;
                else
                {
                    if (wasCharging) { wasCharging = false; settleUntil = r.Time.AddMinutes(SettleMinutes); }
                    settling = r.Time < settleUntil;
                    if (!settling) lastTrusted = r;
                }
            }

            string tip;
            int level = 0;
            IconMode mode;
            if (r.State == MouseState.Ok && r.Charging && r.Percent >= 100)
            {
                mode = IconMode.Normal;
                level = 100;
                tip = Ui.MouseName + ": charged (100%)";
            }
            else if (r.State == MouseState.Ok && r.Charging)
            {
                mode = IconMode.Charging;
                tip = Ui.MouseName + ": charging";
            }
            else if (settling)
            {
                mode = IconMode.Dim;
                level = r.Percent;
                tip = Ui.MouseName + ": settling after charge";
            }
            else if (r.State == MouseState.Ok)
            {
                mode = IconMode.Normal;
                level = r.Percent;
                tip = Ui.MouseName + ": " + r.Percent + "%";
            }
            else if (lastTrusted != null)
            {
                mode = IconMode.Dim;
                level = lastTrusted.Percent;
                tip = Ui.MouseName + ": " + (r.State == MouseState.Asleep ? "asleep" : "not detected") + " (" + lastTrusted.Percent + "%)";
            }
            else
            {
                mode = IconMode.Empty;
                tip = r.State == MouseState.Asleep ? Ui.MouseName + ": asleep or off"
                    : r.State == MouseState.Error ? "Pulsar: read error"
                    : "Pulsar: receiver not found";
            }

            Icon old = currentIcon;
            currentIcon = Ui.MakeIcon(level, mode, SystemInformation.SmallIconSize.Width, Ui.IsTaskbarDark());
            tray.Icon = currentIcon;
            if (old != null) old.Dispose();
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            itemHeader.Text = tip;

            if (r.State == MouseState.Ok && !settling)
            {
                if (r.Charging || r.Percent > LowLevel + 5) lowAlerted = false;
                else if (r.Percent <= LowLevel && !lowAlerted && AlertEnabled())
                {
                    lowAlerted = true;
                    tray.ShowBalloonTip(8000, "Mouse battery low",
                        Ui.MouseName + ": " + r.Percent + "% left. Time to plug it in.", ToolTipIcon.Warning);
                }
            }

            details.Current = r;
            details.LastOk = lastOk;
            details.LastTrusted = lastTrusted;
            details.SettleUntil = settleUntil;
            if (details.Visible) details.Relayout();
        }

        static bool StartupEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void ToggleStartup()
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (k.GetValue(RunName) != null) k.DeleteValue(RunName, false);
                else k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
            }
        }

        // Portable mode: if a portable.ini file sits next to the exe, settings are stored there
        // and nothing is written to the registry (unless the user enables start with Windows).
        static string PortableFile
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "portable.ini"); }
        }

        static string GetSetting(string name, string def)
        {
            try
            {
                if (File.Exists(PortableFile))
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

        static void SetSetting(string name, string value)
        {
            try
            {
                if (File.Exists(PortableFile))
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

        static bool AlertEnabled() { return GetSetting("LowBatteryAlert", "1") != "0"; }
        static void SetAlertEnabled(bool on) { SetSetting("LowBatteryAlert", on ? "1" : "0"); }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // Diagnostic mode: PulsarBattery.exe --once [file] writes a detailed reading, then exits.
            if (args.Length > 0 && args[0] == "--once")
            {
                StringBuilder log = new StringBuilder();
                Reading r = Device.Read(log);
                log.AppendLine("Result: " + r.State + " | " + r.Percent + "% | charging=" + r.Charging + " | " + r.Millivolts + " mV | PID " + r.ProductId.ToString("X4") + (r.Error != null ? " | " + r.Error : ""));
                if (args.Length > 1) File.WriteAllText(args[1], log.ToString(), Encoding.UTF8);
                else Console.Write(log.ToString());
                return r.State == MouseState.Ok ? 0 : 1;
            }

            // Preview mode: PulsarBattery.exe --preview file.png draws the icons and the window into an image.
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
                        states[0] = new Reading(); states[0].State = MouseState.Ok; states[0].Percent = 95; states[0].Charging = true; states[0].Millivolts = 4059; states[0].ProductId = 0xF507;
                        states[1] = new Reading(); states[1].State = MouseState.Ok; states[1].Percent = 88; states[1].Millivolts = 3990; states[1].ProductId = 0xF508;
                        for (int i = 0; i < 2; i++)
                        {
                            using (DetailsForm f = new DetailsForm())
                            using (Bitmap bm = new Bitmap(310, 262))
                            {
                                f.ClientSize = new Size(310, 262);
                                f.Current = states[i];
                                f.LastOk = states[i];
                                if (i == 1) f.showInfoPreview();
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
            using (Mutex mutex = new Mutex(true, "PulsarBatteryTray_SingleInstance", out created))
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
