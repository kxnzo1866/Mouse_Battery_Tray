using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Visual styles: colours, fonts and shapes of one style in light or dark mode.
    //  Theme.P is the palette in use; Theme.For(style, dark) gives any other one (style previews).
    // ------------------------------------------------------------------
    class Palette
    {
        public string Style;
        public bool Dark;
        public Color Bg, Fg, Sub, Track, Border, Surface, Hover, Pressed, Accent, OnAccent, HeaderFg;
        public Color MenuBg, MenuHover, MenuHoverText = Color.Empty;
        public int Radius;                 // buttons (px at 96 dpi); 99 = capsule
        public int CardRadius = 4;         // cards, menu highlight
        public int WindowCorner = 2;       // DWMWA_WINDOW_CORNER_PREFERENCE: 1 square, 2 round, 3 small round
        public bool MenuRounded;           // rounded context menu window (Windows 11)
        public string TitleFont = "Segoe UI Semibold", BigFont = "Segoe UI Light", BodyFont = "Segoe UI", HeaderFont = "Segoe UI Semibold";
        public float TitleSize = 10.5f, BigSize = 32f, HeaderSize = 10.5f;
        public float BarHeight = 8f, BarRadius = 4f;
        public bool Translucent;           // details window behind Acrylic when the effects are on
        public Color Tint = Color.Empty;   // Acrylic tint (alpha = opacity)
        public int SettingsBackdrop;       // DWMWA_SYSTEMBACKDROP_TYPE of the settings window: 0 none, 2 Mica, 3 Acrylic
        public Color Caption = Color.Empty, CaptionText = Color.Empty, FrameBorder = Color.Empty;   // title bar (Windows 11)
        public bool FilledButtons;         // buttons filled with the accent colour (Material)
        public bool Gloss;                 // Aero reflection
        public bool Shine;                 // Liquid Glass highlight and bright edge
        public bool Band;                  // Metro: title on a coloured tile band
        public bool CardRows;              // Material: rows on a raised card
        public bool TextShadow;            // halo behind text drawn over glass
        public bool Square { get { return WindowCorner == 1; } }
    }

    static class StyleBook
    {
        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        static Color Gray(int v) { return Color.FromArgb(v, v, v); }

        // Darkens (or lightens) a colour until white (or black) text reads well on it.
        static Color ForWhiteText(Color c)
        {
            for (int i = 0; i < 12 && Theme.Luminance(c) > 0.42f; i++) c = Theme.Mix(c, Color.Black, 0.12f);
            return c;
        }

        static Color ForDarkBg(Color c)
        {
            for (int i = 0; i < 12 && Theme.Luminance(c) < 0.45f; i++) c = Theme.Mix(c, Color.White, 0.12f);
            return c;
        }

        public static Palette Build(string style, bool dark)
        {
            Palette p = new Palette();
            p.Style = style;
            p.Dark = dark;
            Color src = Theme.SystemAccent;
            switch (style)
            {
                case "fluent":
                    p.Bg = dark ? Gray(32) : Gray(243);
                    p.Fg = dark ? Gray(255) : Gray(27);
                    p.Sub = dark ? Gray(197) : Gray(96);
                    p.Track = dark ? Gray(72) : Gray(212);
                    p.Border = dark ? Gray(64) : Gray(222);
                    p.Surface = dark ? Gray(45) : Gray(251);
                    p.Hover = dark ? Gray(52) : Gray(240);
                    p.Pressed = dark ? Gray(40) : Gray(232);
                    p.Accent = dark ? ForDarkBg(src) : ForWhiteText(src);
                    p.OnAccent = dark ? Color.Black : Color.White;
                    p.MenuBg = dark ? Gray(44) : Gray(249);
                    p.MenuHover = dark ? Gray(61) : Gray(236);
                    p.Radius = 4; p.CardRadius = 8; p.MenuRounded = true;
                    p.TitleFont = "Segoe UI Variable Display Semib"; p.BigFont = "Segoe UI Variable Display Light";
                    p.BodyFont = "Segoe UI Variable Text"; p.HeaderFont = "Segoe UI Variable Display Semib"; p.HeaderSize = 11f;
                    p.BarHeight = 6f; p.BarRadius = 3f;
                    p.Translucent = true;
                    p.Tint = dark ? Color.FromArgb(175, 32, 32, 32) : Color.FromArgb(165, 243, 243, 243);
                    p.SettingsBackdrop = 2;
                    break;

                case "glass":
                    p.Bg = dark ? C(26, 28, 34) : C(232, 236, 243);
                    p.Fg = dark ? C(245, 245, 247) : C(18, 18, 22);
                    p.Sub = dark ? C(168, 172, 182) : C(84, 88, 98);
                    p.Track = dark ? C(62, 66, 78) : C(206, 212, 224);
                    p.Border = dark ? C(78, 84, 98) : C(196, 204, 218);
                    p.Surface = dark ? C(40, 43, 52) : C(248, 250, 253);
                    p.Hover = dark ? C(52, 56, 68) : C(226, 232, 242);
                    p.Pressed = dark ? C(64, 69, 84) : C(212, 220, 234);
                    p.Accent = dark ? C(64, 156, 255) : C(0, 113, 227);
                    p.OnAccent = Color.White;
                    p.MenuBg = dark ? C(36, 39, 47) : C(242, 245, 250);
                    p.MenuHover = dark ? C(58, 63, 77) : C(222, 229, 241);
                    p.Radius = 99; p.CardRadius = 14; p.MenuRounded = true;
                    p.BigFont = "Segoe UI Semilight";
                    p.BarHeight = 8f; p.BarRadius = 4f;
                    p.Translucent = true; p.Shine = true; p.TextShadow = true;
                    p.Tint = dark ? Color.FromArgb(105, 14, 14, 20) : Color.FromArgb(115, 255, 255, 255);
                    p.SettingsBackdrop = 3;
                    p.FrameBorder = dark ? C(96, 102, 118) : C(214, 222, 236);
                    break;

                case "material":
                {
                    // Material You: every colour is a tone of one source colour (the Windows accent colour).
                    Color primary = dark ? ForDarkBg(Theme.Mix(src, Color.White, 0.35f)) : ForWhiteText(src);
                    Color mixTo = dark ? Color.Black : Color.White;
                    p.Bg = Theme.Mix(src, mixTo, dark ? 0.90f : 0.94f);
                    p.Surface = Theme.Mix(src, mixTo, dark ? 0.80f : 0.86f);
                    p.Hover = Theme.Mix(src, mixTo, dark ? 0.72f : 0.78f);
                    p.Pressed = Theme.Mix(src, mixTo, dark ? 0.64f : 0.70f);
                    p.Track = Theme.Mix(src, mixTo, dark ? 0.66f : 0.76f);
                    p.Border = Theme.Mix(src, dark ? Gray(140) : Gray(120), 0.75f);
                    p.Fg = Theme.Mix(src, dark ? Color.White : Color.Black, 0.88f);
                    p.Sub = Theme.Mix(src, dark ? Gray(196) : Gray(72), 0.80f);
                    p.Accent = primary;
                    p.OnAccent = dark ? Theme.Mix(src, Color.Black, 0.82f) : Color.White;
                    p.HeaderFg = primary;
                    p.MenuBg = Theme.Mix(src, mixTo, dark ? 0.84f : 0.90f);
                    p.MenuHover = Theme.Mix(src, mixTo, dark ? 0.70f : 0.78f);
                    p.Radius = 99; p.CardRadius = 12; p.MenuRounded = true;
                    p.FilledButtons = true; p.CardRows = true;
                    p.TitleSize = 11f; p.BigFont = "Segoe UI"; p.BigSize = 30f; p.HeaderSize = 11f;
                    p.BarHeight = 12f; p.BarRadius = 6f;
                    p.Caption = p.Bg; p.CaptionText = p.Fg; p.FrameBorder = p.Bg;
                    break;
                }

                case "metro":
                {
                    Color tile = ForWhiteText(src);
                    p.Bg = dark ? Gray(17) : Color.White;
                    p.Fg = dark ? Color.White : Gray(17);
                    p.Sub = dark ? Gray(160) : Gray(100);
                    p.Track = dark ? Gray(58) : Gray(222);
                    p.Border = dark ? Gray(130) : Gray(120);
                    p.Surface = dark ? Gray(30) : Color.White;
                    p.Hover = dark ? Gray(52) : Gray(226);
                    p.Pressed = tile;
                    p.Accent = dark ? ForDarkBg(src) : tile;
                    p.OnAccent = Color.White;
                    p.MenuBg = dark ? Gray(30) : Color.White;
                    p.MenuHover = tile;
                    p.MenuHoverText = Color.White;
                    p.Radius = 0; p.CardRadius = 0; p.WindowCorner = 1;
                    p.TitleFont = "Segoe UI Light"; p.TitleSize = 13f; p.BigFont = "Segoe UI Light"; p.BigSize = 34f;
                    p.BodyFont = "Segoe UI"; p.HeaderFont = "Segoe UI Light"; p.HeaderSize = 15f;
                    p.BarHeight = 6f; p.BarRadius = 0f;
                    p.Band = true;
                    p.Caption = tile; p.CaptionText = Color.White; p.FrameBorder = tile;
                    break;
                }

                case "aero":
                    p.Bg = dark ? C(22, 30, 42) : C(238, 243, 250);
                    p.Fg = dark ? C(240, 244, 250) : C(24, 28, 34);
                    p.Sub = dark ? C(150, 166, 188) : C(84, 96, 114);
                    p.Track = dark ? C(48, 60, 78) : C(212, 221, 233);
                    p.Border = dark ? C(88, 114, 150) : C(112, 138, 170);
                    p.Surface = dark ? C(40, 52, 70) : C(246, 249, 253);
                    p.Hover = dark ? C(52, 74, 104) : C(212, 234, 252);
                    p.Pressed = dark ? C(62, 90, 128) : C(184, 218, 246);
                    p.Accent = dark ? C(120, 186, 255) : C(0, 84, 166);
                    p.OnAccent = Color.White;
                    p.HeaderFg = dark ? C(150, 200, 255) : C(0, 51, 153);
                    p.MenuBg = dark ? C(30, 40, 56) : C(241, 245, 251);
                    p.MenuHover = dark ? C(52, 78, 114) : C(216, 235, 252);
                    p.Radius = 3; p.CardRadius = 4;
                    p.TitleFont = "Segoe UI"; p.TitleSize = 11f; p.BigFont = "Segoe UI"; p.BigSize = 30f;
                    p.HeaderFont = "Segoe UI"; p.HeaderSize = 11.5f;
                    p.BarHeight = 10f; p.BarRadius = 2f;
                    p.Translucent = true; p.Gloss = true; p.TextShadow = true;
                    p.Tint = dark ? Color.FromArgb(175, 18, 40, 78) : Color.FromArgb(145, 160, 198, 240);
                    p.Caption = dark ? C(32, 54, 88) : C(176, 205, 238);
                    p.CaptionText = p.Fg;
                    p.FrameBorder = dark ? C(70, 110, 170) : C(96, 136, 186);
                    break;

                default:   // classic: the original look
                    p.Style = "classic";
                    p.Bg = dark ? Gray(32) : Gray(249);
                    p.Fg = dark ? Gray(242) : Gray(26);
                    p.Sub = dark ? Gray(160) : Gray(96);
                    p.Track = dark ? Gray(62) : Gray(222);
                    p.Border = dark ? Gray(70) : Gray(200);
                    p.Surface = dark ? Gray(45) : Color.White;
                    p.Hover = dark ? Gray(62) : Gray(229);
                    p.Pressed = p.Track;
                    p.Accent = dark ? C(96, 205, 255) : C(0, 95, 184);
                    p.OnAccent = dark ? Color.Black : Color.White;
                    p.MenuBg = dark ? Gray(43) : Gray(249);
                    p.MenuHover = p.Hover;
                    p.Radius = 0;
                    break;
            }
            if (p.HeaderFg.IsEmpty) p.HeaderFg = p.Fg;
            return p;
        }
    }

    // ------------------------------------------------------------------
    //  Window attributes: DWM (corners, title bar, Mica) and Acrylic blur.
    // ------------------------------------------------------------------
    static class Dwm
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int State, Flags, Gradient, Animation; }
        [StructLayout(LayoutKind.Sequential)]
        struct CompositionData { public int Attribute; public IntPtr Data; public int Size; }
        [DllImport("user32.dll")]
        static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);

        public const int DarkMode = 20, DarkModeOld = 19, Corner = 33, BorderColor = 34, CaptionColor = 35, TextColor = 36, Backdrop = 38;

        public static bool SetInt(IntPtr h, int attr, int value)
        {
            try { return DwmSetWindowAttribute(h, attr, ref value, 4) == 0; }
            catch { return false; }
        }

        // Color.Empty = Windows default.
        public static void SetColor(IntPtr h, int attr, Color c)
        {
            SetInt(h, attr, c.IsEmpty ? unchecked((int)0xFFFFFFFF) : (c.B << 16) | (c.G << 8) | c.R);
        }

        // Acrylic blur behind the whole window (Windows 10 1803+). Color.Empty turns it off.
        // While it is on, the window paints its background fully transparent (alpha 0).
        public static bool SetAcrylic(IntPtr h, Color tint)
        {
            AccentPolicy a = new AccentPolicy();
            a.State = tint.IsEmpty ? 0 : 4;   // ACCENT_DISABLED / ACCENT_ENABLE_ACRYLICBLURBEHIND
            a.Flags = 2;
            if (!tint.IsEmpty) a.Gradient = (tint.A << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;
            int size = Marshal.SizeOf(a);
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(a, mem, false);
                CompositionData d = new CompositionData();
                d.Attribute = 19;   // WCA_ACCENT_POLICY
                d.Data = mem;
                d.Size = size;
                return SetWindowCompositionAttribute(h, ref d) != 0;
            }
            catch { return false; }
            finally { Marshal.FreeHGlobal(mem); }
        }
    }

    // ------------------------------------------------------------------
    //  Button drawn in the current style (square, rounded, capsule, filled, glossy).
    // ------------------------------------------------------------------
    class StyledButton : Button
    {
        bool hot, down;

        public StyledButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { hot = false; down = false; base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.P;
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            g.Clear(Parent != null ? Parent.BackColor : p.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float bw = p.Style == "metro" ? Math.Max(2f, (float)Math.Round(2 * s)) : Math.Max(1f, (float)Math.Round(s));
            RectangleF r = new RectangleF(bw / 2f, bw / 2f, Width - bw, Height - bw);
            float radius = p.Radius >= 99 ? r.Height / 2f : p.Radius * s;
            bool filled = p.FilledButtons || (p.Style == "metro" && down);
            Color fill, text, border;
            if (!Enabled)
            {
                fill = filled ? Theme.Mix(p.Accent, p.Bg, 0.65f) : p.Surface;
                text = p.Sub;
                border = Theme.Mix(p.Border, p.Bg, 0.4f);
            }
            else if (filled)
            {
                fill = down ? Theme.Mix(p.Accent, p.OnAccent, 0.22f) : hot ? Theme.Mix(p.Accent, p.OnAccent, 0.12f) : p.Accent;
                text = p.OnAccent;
                border = fill;
            }
            else
            {
                fill = down ? p.Pressed : hot ? p.Hover : p.Surface;
                text = p.Fg;
                border = p.Style == "metro" && hot ? p.Fg : p.Border;
            }

            using (GraphicsPath path = Ui.RoundRect(r, radius))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, path);
                if (p.Gloss || p.Shine)
                {
                    // Aero: brighter upper half; Liquid Glass: soft highlight fading downwards.
                    Region old = g.Clip;
                    g.SetClip(path, CombineMode.Intersect);
                    RectangleF top = new RectangleF(r.X, r.Y, r.Width, p.Gloss ? r.Height / 2f : r.Height * 0.7f);
                    int a1 = p.Gloss ? (p.Dark ? 60 : 170) : (p.Dark ? 40 : 120);
                    int a2 = p.Gloss ? (p.Dark ? 20 : 70) : 0;
                    using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(top.X, top.Y - 1, top.Width, top.Height + 2),
                               Color.FromArgb(a1, Color.White), Color.FromArgb(a2, Color.White), 90f))
                        g.FillRectangle(lb, top);
                    g.Clip = old;
                }
                if (!filled || p.Gloss)
                    using (Pen pen = new Pen(border, bw)) g.DrawPath(pen, path);
                if (Focused && ShowFocusCues)
                {
                    float i = 3 * s;
                    RectangleF fr = new RectangleF(r.X + i, r.Y + i, r.Width - 2 * i, r.Height - 2 * i);
                    using (GraphicsPath fp = Ui.RoundRect(fr, Math.Max(0, radius - i)))
                    using (Pen pen = new Pen(filled ? p.OnAccent : p.Accent, Math.Max(1f, s)))
                    {
                        pen.DashStyle = DashStyle.Dot;
                        g.DrawPath(pen, fp);
                    }
                }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // ------------------------------------------------------------------
    //  Tabs of the settings window, drawn in the current style.
    // ------------------------------------------------------------------
    class TabStrip : Control
    {
        string[] items = new string[0];
        readonly List<Rectangle> boxes = new List<Rectangle>();
        int selected, hot = -1;
        public event EventHandler SelectedChanged;

        public TabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= items.Length || value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        public void SetItems(string[] texts)
        {
            items = texts;
            Measure();
        }

        float DpiScale { get { using (Graphics g = CreateGraphics()) return g.DpiX / 96f; } }

        Font TabFont(Palette p)
        {
            return p.Style == "metro" ? Theme.MakeFont("Segoe UI Light", 15f) : Theme.MakeFont(p.BodyFont, 10.5f);
        }

        // Places the tabs and sizes the control (called again when the texts or the style change).
        public void Measure()
        {
            Palette p = Theme.P;
            float s = DpiScale;
            int padX = (int)Math.Round((p.Style == "metro" ? 2 : 14) * s);
            int gap = (int)Math.Round((p.Style == "metro" ? 22 : 4) * s);
            int padY = (int)Math.Round(7 * s);
            boxes.Clear();
            int x = 0, h = 0;
            using (Font f = TabFont(p))
            {
                foreach (string t in items)
                {
                    Size ts = TextRenderer.MeasureText(t, f, Size.Empty, TextFormatFlags.NoPadding);
                    h = Math.Max(h, ts.Height + 2 * padY + (int)Math.Round(3 * s));
                    boxes.Add(new Rectangle(x, 0, ts.Width + 2 * padX, 0));
                    x += ts.Width + 2 * padX + gap;
                }
            }
            for (int i = 0; i < boxes.Count; i++) boxes[i] = new Rectangle(boxes[i].X, 0, boxes[i].Width, h);
            Size = new Size(Math.Max(1, x - gap), Math.Max(1, h));
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette p = Theme.P;
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            g.Clear(p.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float line = Math.Max(1f, (float)Math.Round(s));
            bool baseline = p.Style != "metro" && p.Style != "material" && p.Style != "glass";
            if (baseline)
                using (SolidBrush b = new SolidBrush(p.Border)) g.FillRectangle(b, 0, Height - line, Width, line);

            using (Font f = TabFont(p))
            {
                for (int i = 0; i < boxes.Count; i++)
                {
                    Rectangle b = boxes[i];
                    bool sel = i == selected;
                    if (sel)
                    {
                        RectangleF pill = new RectangleF(b.X + 0.5f, b.Y + 2 * s, b.Width - 1, b.Height - 6 * s);
                        switch (p.Style)
                        {
                            case "material":
                                using (GraphicsPath gp = Ui.RoundRect(pill, pill.Height / 2f))
                                using (SolidBrush br = new SolidBrush(p.Hover)) g.FillPath(br, gp);
                                break;
                            case "glass":
                                using (GraphicsPath gp = Ui.RoundRect(pill, pill.Height / 2f))
                                {
                                    using (SolidBrush br = new SolidBrush(p.Surface)) g.FillPath(br, gp);
                                    using (Pen pen = new Pen(p.Border, line)) g.DrawPath(pen, gp);
                                }
                                break;
                            case "aero":
                            {
                                RectangleF tab = new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height);
                                using (GraphicsPath gp = Ui.RoundRect(tab, 3 * s))
                                {
                                    using (LinearGradientBrush br = new LinearGradientBrush(tab, Theme.Mix(p.Surface, Color.White, p.Dark ? 0.1f : 0.6f), p.Surface, 90f))
                                        g.FillPath(br, gp);
                                    using (Pen pen = new Pen(p.Border, line)) g.DrawPath(pen, gp);
                                }
                                using (SolidBrush br = new SolidBrush(p.Surface)) g.FillRectangle(br, b.X + line, Height - line, b.Width - 2 * line, line);
                                break;
                            }
                            case "fluent":
                            {
                                float w = Math.Min(b.Width - 8 * s, 18 * s), hh = 3 * s;
                                using (GraphicsPath gp = Ui.RoundRect(new RectangleF(b.X + (b.Width - w) / 2f, Height - hh, w, hh), hh / 2f))
                                using (SolidBrush br = new SolidBrush(p.Accent)) g.FillPath(br, gp);
                                break;
                            }
                            case "classic":
                                using (SolidBrush br = new SolidBrush(p.Accent)) g.FillRectangle(br, b.X, Height - 2 * line, b.Width, 2 * line);
                                break;
                        }
                    }
                    Color c = sel || i == hot ? p.Fg : p.Sub;
                    if (sel && p.Style == "metro") c = p.Accent;
                    TextRenderer.DrawText(g, items[i], f, new Rectangle(b.X, b.Y, b.Width, b.Height - (int)Math.Round(2 * s)), c,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (sel && Focused && ShowFocusCues)
                        ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(new Rectangle(b.X, b.Y, b.Width, b.Height - (int)(3 * s)), -2, -2), p.Fg, p.Bg);
                }
            }
        }

        int HitTest(Point pt)
        {
            for (int i = 0; i < boxes.Count; i++) if (boxes[i].Contains(pt)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && h >= 0) { Focus(); Selected = h; }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) Selected = selected - 1;
            if (e.KeyCode == Keys.Right) Selected = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    // ------------------------------------------------------------------
    //  One choice of the Styles tab: a live preview of the details window in that style.
    // ------------------------------------------------------------------
    class StyleCard : Control
    {
        public readonly string Key;
        public string Title = "", Description = "";
        public event EventHandler Picked;
        bool selected, hot;

        static Bitmap wall, wallBlur;
        static readonly Reading Sample = MakeSample();

        public StyleCard(string key)
        {
            Key = key;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        static Reading MakeSample()
        {
            Reading r = new Reading();
            r.State = MouseState.Ok;
            r.Percent = 72;
            r.Millivolts = 3920;
            r.Name = "Wireless Mouse";
            r.Connection = "2.4 GHz wireless receiver";
            return r;
        }

        public bool Selected
        {
            get { return selected; }
            set { if (selected != value) { selected = value; Invalidate(); } }
        }

        // Sample wallpaper behind the previews (shared by all cards), and its blurred copy for Acrylic.
        static void EnsureWallpaper(Size size)
        {
            if (wall != null && wall.Size == size) return;
            if (wall != null) { wall.Dispose(); wallBlur.Dispose(); }
            wall = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(wall)) Ui.DrawWallpaper(g, new Rectangle(Point.Empty, size));
            wallBlur = Ui.Blur(wall, 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette cur = Theme.P;
            Palette look = Theme.For(Key, Theme.Dark);
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            g.Clear(Parent != null ? Parent.BackColor : cur.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            float bw = selected ? Math.Max(2f, (float)Math.Round(2 * s)) : Math.Max(1f, (float)Math.Round(s));
            RectangleF card = new RectangleF(bw / 2f, bw / 2f, Width - bw, Height - bw);
            float radius = Math.Min(cur.CardRadius, 12) * s;
            using (GraphicsPath cp = Ui.RoundRect(card, radius))
            {
                using (SolidBrush b = new SolidBrush(hot && !selected ? cur.Hover : cur.Surface)) g.FillPath(b, cp);
                using (Pen pen = new Pen(selected ? cur.Accent : cur.Border, bw)) g.DrawPath(pen, cp);
            }

            int pad = (int)Math.Round(8 * s);
            Rectangle strip = new Rectangle(pad, pad, Width - 2 * pad, (int)Math.Round(108 * s));
            EnsureWallpaper(strip.Size);
            using (GraphicsPath sp = Ui.RoundRect(strip, Math.Max(0, radius - pad / 2f)))
            {
                Region old = g.Clip;
                g.SetClip(sp, CombineMode.Intersect);
                g.DrawImageUnscaled(wall, strip.Location);
                float wh = strip.Height - 18 * s;
                float ww = wh * 310f / 237f;
                RectangleF win = new RectangleF(strip.X + (strip.Width - ww) / 2f, strip.Y + 9 * s, ww, wh);
                DetailsForm.PaintPreview(g, win, look, Theme.Effects && look.Translucent, Sample, null, wallBlur, strip);
                g.Clip = old;
            }

            if (selected)
            {
                // Check badge in the corner of the preview.
                float d = 18 * s;
                RectangleF c = new RectangleF(strip.Right - d - 5 * s, strip.Y + 5 * s, d, d);
                using (SolidBrush b = new SolidBrush(cur.Accent)) g.FillEllipse(b, c);
                using (Pen pen = new Pen(cur.OnAccent, 1.8f * s))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new PointF[] {
                        new PointF(c.X + d * 0.28f, c.Y + d * 0.52f),
                        new PointF(c.X + d * 0.44f, c.Y + d * 0.68f),
                        new PointF(c.X + d * 0.73f, c.Y + d * 0.35f) });
                }
            }

            int ty = strip.Bottom + (int)Math.Round(6 * s);
            using (Font bold = Theme.MakeFont(cur.Style == "metro" ? "Segoe UI Semilight" : "Segoe UI Semibold", 9.5f))
                TextRenderer.DrawText(g, Title, bold, new Rectangle(pad, ty, Width - 2 * pad, (int)Math.Round(20 * s)), cur.Fg,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            ty += (int)Math.Round(20 * s);
            TextRenderer.DrawText(g, Description, Font, new Rectangle(pad, ty, Width - 2 * pad, Height - ty - pad / 2), cur.Sub,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -(int)(3 * s), -(int)(3 * s)), cur.Fg, cur.Surface);
        }

        void Pick()
        {
            if (Picked != null) Picked(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { Focus(); Pick(); }
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys k) { return k == Keys.Enter || base.IsInputKey(k); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Pick(); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }
}
