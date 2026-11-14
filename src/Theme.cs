using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Light / dark theme of the app windows and menu ("system" follows Windows).
    //  The tray icon itself always follows the taskbar colour (see Ui.IsTaskbarDark).
    // ------------------------------------------------------------------
    static class Theme
    {
        public static readonly string[] Modes = { "system", "light", "dark" };
        public static string Mode = "system";
        public static event EventHandler Changed;

        public static string Normalize(string mode)
        {
            return mode == "light" || mode == "dark" ? mode : "system";
        }

        public static void Set(string mode)
        {
            Mode = Normalize(mode);
            Notify();
        }

        public static void Notify()
        {
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        public static bool Dark
        {
            get { return Mode == "dark" || (Mode == "system" && Ui.IsDark()); }
        }

        public static Color Bg { get { return Dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(249, 249, 249); } }
        public static Color Fg { get { return Dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(26, 26, 26); } }
        public static Color Sub { get { return Dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96); } }
        public static Color Track { get { return Dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(222, 222, 222); } }
        public static Color Border { get { return Dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200); } }
        public static Color Surface { get { return Dark ? Color.FromArgb(45, 45, 45) : Color.White; } }
        public static Color Hover { get { return Dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(229, 229, 229); } }
        public static Color Accent { get { return Dark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 95, 184); } }
        public static Color MenuBg { get { return Dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249); } }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // Dark or light title bar (DWMWA_USE_IMMERSIVE_DARK_MODE = 20, or 19 on early Windows 10 builds).
        public static void ApplyTitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            try
            {
                int v = Dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                // Redraw the frame so the change shows at once (NOSIZE | NOMOVE | NOZORDER | NOACTIVATE | FRAMECHANGED).
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
            catch { }
        }

        // Native parts drawn by Windows (combo box drop-downs, scroll bars).
        public static void ApplyNative(Control c, bool comboBox)
        {
            if (!c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, Dark ? (comboBox ? "DarkMode_CFD" : "DarkMode_Explorer") : "Explorer", null); }
            catch { }
        }

        public static ToolStripRenderer MenuRenderer()
        {
            return new ThemedMenuRenderer(Dark);
        }
    }

    class ThemedMenuColors : ProfessionalColorTable
    {
        readonly Color bg, hover, border;

        public ThemedMenuColors(bool dark)
        {
            UseSystemColors = false;
            bg = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(249, 249, 249);
            hover = dark ? Color.FromArgb(62, 62, 62) : Color.FromArgb(229, 229, 229);
            border = dark ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);
        }

        public override Color ToolStripDropDownBackground { get { return bg; } }
        public override Color ImageMarginGradientBegin { get { return bg; } }
        public override Color ImageMarginGradientMiddle { get { return bg; } }
        public override Color ImageMarginGradientEnd { get { return bg; } }
        public override Color MenuBorder { get { return border; } }
        public override Color MenuItemBorder { get { return hover; } }
        public override Color MenuItemSelected { get { return hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return hover; } }
        public override Color MenuItemPressedGradientBegin { get { return hover; } }
        public override Color MenuItemPressedGradientEnd { get { return hover; } }
        public override Color SeparatorDark { get { return border; } }
        public override Color SeparatorLight { get { return border; } }
        public override Color CheckBackground { get { return bg; } }
        public override Color CheckSelectedBackground { get { return hover; } }
        public override Color CheckPressedBackground { get { return hover; } }
    }

    class ThemedMenuRenderer : ToolStripProfessionalRenderer
    {
        readonly Color text, disabled;

        public ThemedMenuRenderer(bool dark) : base(new ThemedMenuColors(dark))
        {
            RoundedEdges = false;
            text = dark ? Color.FromArgb(242, 242, 242) : Color.FromArgb(26, 26, 26);
            disabled = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(96, 96, 96);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? text : disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = text;
            base.OnRenderArrow(e);
        }

        // Check mark drawn in the text colour, so it stays visible on a dark menu.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen p = new Pen(text, Math.Max(1.5f, r.Height / 9f)))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                g.DrawLines(p, new PointF[] {
                    new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.76f),
                    new PointF(r.Left + r.Width * 0.84f, r.Top + r.Height * 0.26f) });
            }
        }
    }
}
