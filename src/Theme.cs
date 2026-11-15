using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Light / dark theme ("system" follows Windows) and visual style of the app windows and menu.
    //  The colours of each style are in Styles.cs (StyleBook). The tray icon itself always follows
    //  the taskbar colour (see Ui.IsTaskbarDark).
    // ------------------------------------------------------------------
    static class Theme
    {
        public static readonly string[] Modes = { "system", "light", "dark" };
        public static readonly string[] Styles = { "classic", "fluent", "glass", "material", "metro", "aero" };
        public static string Mode = "system";
        public static string Style = "classic";
        public static bool Effects = true;      // transparency effects (Mica, Acrylic) wanted by the user
        public static event EventHandler Changed;

        static readonly Dictionary<string, Palette> cache = new Dictionary<string, Palette>();
        static readonly Dictionary<string, bool> fonts = new Dictionary<string, bool>();
        static Color accent = Color.Empty;
        static int build = -1;

        public static string Normalize(string mode)
        {
            return mode == "light" || mode == "dark" ? mode : "system";
        }

        public static string NormalizeStyle(string style)
        {
            return Array.IndexOf(Styles, style) >= 0 ? style : "classic";
        }

        public static void Set(string mode)
        {
            Mode = Normalize(mode);
            Notify();
        }

        public static void SetStyle(string style, bool effects)
        {
            Style = NormalizeStyle(style);
            Effects = effects;
            Notify();
        }

        // Also called when a Windows setting changes (light/dark, accent colour, transparency).
        public static void Notify()
        {
            cache.Clear();
            accent = Color.Empty;
            if (Changed != null) Changed(null, EventArgs.Empty);
        }

        public static bool Dark
        {
            get { return Mode == "dark" || (Mode == "system" && Ui.IsDark()); }
        }

        public static Palette P { get { return For(Style, Dark); } }

        public static Palette For(string style, bool dark)
        {
            string key = style + (dark ? "/dark" : "/light");
            Palette p;
            if (!cache.TryGetValue(key, out p))
            {
                p = StyleBook.Build(NormalizeStyle(style), dark);
                cache[key] = p;
            }
            return p;
        }

        public static Color Bg { get { return P.Bg; } }
        public static Color Fg { get { return P.Fg; } }
        public static Color Sub { get { return P.Sub; } }
        public static Color Track { get { return P.Track; } }
        public static Color Border { get { return P.Border; } }
        public static Color Surface { get { return P.Surface; } }
        public static Color Hover { get { return P.Hover; } }
        public static Color Accent { get { return P.Accent; } }
        public static Color MenuBg { get { return P.MenuBg; } }

        // ---- Windows information ----

        // Windows accent colour (source colour of Material You, Fluent and Metro accents).
        public static Color SystemAccent
        {
            get
            {
                if (!accent.IsEmpty) return accent;
                accent = Color.FromArgb(0, 120, 215);
                try
                {
                    object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM", "AccentColor", null);
                    if (v is int)
                    {
                        int abgr = (int)v;
                        accent = Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);
                    }
                }
                catch { }
                return accent;
            }
        }

        // Windows build number (22000+ = Windows 11). Read from the registry: Environment.OSVersion lies without a manifest.
        public static int Build
        {
            get
            {
                if (build >= 0) return build;
                build = 0;
                try
                {
                    object v = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber", "0");
                    int.TryParse(v as string, out build);
                }
                catch { }
                return build;
            }
        }

        // "Transparency effects" switch of Windows (Personalization > Colours).
        public static bool WindowsTransparency
        {
            get
            {
                try
                {
                    object v = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1);
                    return !(v is int) || (int)v != 0;
                }
                catch { return true; }
            }
        }

        // Acrylic needs Windows 10 1803+ and the Windows transparency effects on.
        public static bool EffectsAvailable { get { return Build >= 17134 && WindowsTransparency; } }

        public static bool LiveTranslucent(Palette p)
        {
            return p.Translucent && Effects && EffectsAvailable;
        }

        // ---- Colours and fonts ----

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static float Luminance(Color c)
        {
            return (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;
        }

        // Font of a style, with a Segoe UI fallback when the family is not installed (Segoe UI Variable before Windows 11).
        public static Font MakeFont(string family, float size)
        {
            return MakeFont(family, size, FontStyle.Regular);
        }

        public static Font MakeFont(string family, float size, FontStyle style)
        {
            bool ok;
            if (!fonts.TryGetValue(family, out ok))
            {
                try { using (FontFamily ff = new FontFamily(family)) ok = true; }
                catch { ok = false; }
                fonts[family] = ok;
            }
            if (!ok)
            {
                if (family.Contains("Semib")) family = "Segoe UI Semibold";
                else if (family.Contains("Semil")) family = "Segoe UI Semilight";
                else if (family.Contains("Light")) family = "Segoe UI Light";
                else family = "Segoe UI";
            }
            return new Font(family, size, style);
        }

        // ---- Windows ----

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // Title bar of a normal window: dark or light, plus the style (corners, colours, Mica / Acrylic on Windows 11).
        public static void ApplyTitleBar(Form f)
        {
            if (!f.IsHandleCreated) return;
            try
            {
                IntPtr h = f.Handle;
                if (!Dwm.SetInt(h, Dwm.DarkMode, Dark ? 1 : 0)) Dwm.SetInt(h, Dwm.DarkModeOld, Dark ? 1 : 0);
                if (Build >= 22000)
                {
                    Palette p = P;
                    Dwm.SetInt(h, Dwm.Corner, p.Square ? 1 : 0);
                    Dwm.SetColor(h, Dwm.BorderColor, p.FrameBorder);
                    Dwm.SetColor(h, Dwm.CaptionColor, p.Caption);
                    Dwm.SetColor(h, Dwm.TextColor, p.CaptionText);
                    if (Build >= 22621)
                        Dwm.SetInt(h, Dwm.Backdrop, p.SettingsBackdrop != 0 && Effects && WindowsTransparency ? p.SettingsBackdrop : 0);
                }
                // Redraw the frame so the change shows at once (NOSIZE | NOMOVE | NOZORDER | NOACTIVATE | FRAMECHANGED).
                SetWindowPos(h, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
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
            return new ThemedMenuRenderer(P);
        }

        public static Font MenuFont()
        {
            Palette p = P;
            return MakeFont(p.Style == "metro" ? "Segoe UI Semilight" : p.BodyFont, p.Style == "metro" ? 10f : 9f);
        }

        // Rounded or square menu window (Windows 11 only; nothing changes on Windows 10).
        public static void ApplyMenuWindow(ToolStripDropDown d)
        {
            if (d == null || !d.IsHandleCreated || Build < 22000) return;
            Palette p = P;
            Dwm.SetInt(d.Handle, Dwm.Corner, p.Style == "classic" ? 0 : p.MenuRounded ? 3 : 1);
        }
    }

    class ThemedMenuColors : ProfessionalColorTable
    {
        readonly Color bg, hover, border;

        public ThemedMenuColors(Palette p)
        {
            UseSystemColors = false;
            bg = p.MenuBg;
            hover = p.MenuHover;
            border = p.Border;
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
        readonly Palette p;
        readonly bool classic;

        public ThemedMenuRenderer(Palette palette) : base(new ThemedMenuColors(palette))
        {
            p = palette;
            classic = p.Style == "classic";
            RoundedEdges = false;
        }

        Color TextColor(ToolStripItem item)
        {
            if (!item.Enabled) return p.Sub;
            if (item.Selected && !p.MenuHoverText.IsEmpty) return p.MenuHoverText;
            return p.Fg;
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (classic || !(e.Item.Owner is ToolStripDropDown)) { base.OnRenderMenuItemBackground(e); return; }
            if (!e.Item.Selected || !e.Item.Enabled) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Size sz = e.Item.Size;
            switch (p.Style)
            {
                case "metro":
                    using (SolidBrush b = new SolidBrush(p.MenuHover)) g.FillRectangle(b, 0, 0, sz.Width, sz.Height);
                    break;
                case "aero":
                {
                    RectangleF r = new RectangleF(2.5f, 0.5f, sz.Width - 5f, sz.Height - 1f);
                    using (GraphicsPath path = Ui.RoundRect(r, 3f))
                    {
                        using (LinearGradientBrush lb = new LinearGradientBrush(r, Theme.Mix(p.MenuHover, Color.White, p.Dark ? 0.12f : 0.55f), p.MenuHover, 90f))
                            g.FillPath(lb, path);
                        using (Pen pen = new Pen(Color.FromArgb(150, p.Accent))) g.DrawPath(pen, path);
                    }
                    break;
                }
                default:
                {
                    RectangleF r = new RectangleF(4f, 1f, sz.Width - 8f, sz.Height - 2f);
                    float radius = Math.Min(r.Height / 2f, p.Radius >= 99 ? r.Height / 2f : Math.Max(4f, p.Radius));
                    using (GraphicsPath path = Ui.RoundRect(r, radius))
                    using (SolidBrush b = new SolidBrush(p.MenuHover)) g.FillPath(b, path);
                    break;
                }
            }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (classic || !(e.ToolStrip is ToolStripDropDown)) { base.OnRenderToolStripBackground(e); return; }
            Rectangle r = new Rectangle(Point.Empty, e.ToolStrip.Size);
            if (p.Gloss || p.Shine)
            {
                Color top = Theme.Mix(p.MenuBg, Color.White, p.Dark ? 0.06f : 0.5f);
                using (LinearGradientBrush lb = new LinearGradientBrush(r, top, p.MenuBg, 90f)) e.Graphics.FillRectangle(lb, r);
            }
            else using (SolidBrush b = new SolidBrush(p.MenuBg)) e.Graphics.FillRectangle(b, r);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            if (classic) base.OnRenderImageMargin(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Windows 11 draws its own border around rounded menus.
            if (!classic && p.MenuRounded && Theme.Build >= 22000) return;
            if (classic || !(e.ToolStrip is ToolStripDropDown)) { base.OnRenderToolStripBorder(e); return; }
            using (Pen pen = new Pen(p.Style == "metro" ? p.Accent : p.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (classic) { base.OnRenderSeparator(e); return; }
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(p.Border)) e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = TextColor(e.Item);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = e.Item != null ? TextColor(e.Item) : p.Fg;
            base.OnRenderArrow(e);
        }

        // Check mark drawn in the text colour, so it stays visible on a dark menu.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            Rectangle r = e.ImageRectangle;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(TextColor(e.Item), Math.Max(1.5f, r.Height / 9f)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                g.DrawLines(pen, new PointF[] {
                    new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.76f),
                    new PointF(r.Left + r.Width * 0.84f, r.Top + r.Height * 0.26f) });
            }
        }
    }
}
