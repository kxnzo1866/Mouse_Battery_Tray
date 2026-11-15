using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Details window (left click on the icon). Fully drawn in OnPaint, in the current visual style;
    //  glass styles (Fluent, Liquid Glass, Aero) sit on a real Acrylic blur when the effects are on.
    // ------------------------------------------------------------------
    class DetailsForm : Form
    {
        public Reading Current = new Reading();
        public DeviceTrack Track;     // history of the displayed mouse (may be null)
        public DateTime HiddenAt = DateTime.MinValue;
        public event EventHandler SettingsRequested;

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out RECT r);

        readonly ToolTip gearTip = new ToolTip();
        bool gearHot;
        bool transparent;             // Acrylic on: the background is painted fully transparent
        float scale = 1f;

        // Paint modes of PaintContent
        public const int Opaque = 0, LiveAcrylic = 1, BackdropDrawn = 2;

        public DetailsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            BackColor = Color.Black;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            Lang.Changed += OnLangChanged;
            Theme.Changed += OnThemeChanged;
        }

        void OnLangChanged(object sender, EventArgs e)
        {
            if (gearHot) gearTip.SetToolTip(this, Lang.T("Gear.Tip"));
            if (Visible) Relayout();
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            ApplyWindowStyle();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Lang.Changed -= OnLangChanged;
                Theme.Changed -= OnThemeChanged;
                gearTip.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowStyle();
        }

        // Corners and Acrylic of the current style.
        void ApplyWindowStyle()
        {
            if (!IsHandleCreated) return;
            Palette p = Theme.P;
            Dwm.SetInt(Handle, Dwm.Corner, p.WindowCorner);
            bool want = Theme.LiveTranslucent(p);
            transparent = want && Dwm.SetAcrylic(Handle, p.Tint);
            if (!transparent) Dwm.SetAcrylic(Handle, Color.Empty);
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

        // ---- Gear (top-right corner): opens the settings window ----
        static RectangleF GearRect(float width, float s, Palette p)
        {
            float size = 20 * s;
            return new RectangleF(width - 18 * s - size, (p.Band ? 10 : 13) * s, size, size);
        }

        bool OnGear(Point pt)
        {
            RectangleF r = GearRect(ClientSize.Width, scale, Theme.P);
            r.Inflate(4 * scale, 4 * scale);
            return r.Contains(pt);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool hot = OnGear(e.Location);
            if (hot != gearHot)
            {
                gearHot = hot;
                Cursor = hot ? Cursors.Hand : Cursors.Default;
                gearTip.SetToolTip(this, hot ? Lang.T("Gear.Tip") : null);
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (gearHot) { gearHot = false; Cursor = Cursors.Default; gearTip.SetToolTip(this, null); Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && OnGear(e.Location) && SettingsRequested != null) SettingsRequested(this, EventArgs.Empty);
            base.OnMouseClick(e);
        }

        public void ShowNearTray()
        {
            Relayout();
            Show();
            Activate();
            Invalidate();
        }

        // Size and position: the window has one extra row while charging (level).
        public void Relayout()
        {
            float s;
            using (Graphics g = CreateGraphics()) s = g.DpiX / 96f;
            scale = s;
            bool tall = Current.State == MouseState.Ok && Current.Charging;
            Size = new Size((int)(310 * s), (int)((tall ? 262 : 237) * s));
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

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintContent(e.Graphics, ClientSize.Width, ClientSize.Height, Theme.P, transparent ? LiveAcrylic : Opaque, Current, Track, gearHot);
        }

        // Opaque rendering (preview sheet).
        public void PaintTo(Graphics g)
        {
            PaintContent(g, ClientSize.Width, ClientSize.Height, Theme.P, Opaque, Current, Track, false);
        }

        // Miniature of the window in a given style, drawn over a sample wallpaper (Styles tab, preview sheet).
        // translucent: show the Acrylic look (blurred wallpaper + tint) instead of the opaque fallback.
        public static void PaintPreview(Graphics g, RectangleF dest, Palette p, bool translucent, Reading r, DeviceTrack track,
                                        Image blurredWall, Rectangle wallRect)
        {
            float s = g.DpiX / 96f;
            bool tall = r.State == MouseState.Ok && r.Charging;
            float lw = 310 * s, lh = (tall ? 262 : 237) * s;
            float k = dest.Width / lw;
            float corner = p.Square ? 0 : 8 * s * k;
            GraphicsState state = g.Save();
            using (GraphicsPath win = Ui.RoundRect(new RectangleF(dest.X, dest.Y, dest.Width, lh * k), corner))
            {
                // Soft shadow under the window.
                for (int i = 3; i >= 1; i--)
                    using (GraphicsPath sh = Ui.RoundRect(new RectangleF(dest.X - i * 0.5f, dest.Y + i, dest.Width + i, lh * k + i * 0.6f), corner + i))
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(22, 0, 0, 0)))
                        g.FillPath(b, sh);
                g.SetClip(win, CombineMode.Intersect);
                if (translucent && blurredWall != null) g.DrawImage(blurredWall, wallRect);
                g.TranslateTransform(dest.X, dest.Y);
                g.ScaleTransform(k, k);
                PaintContent(g, lw, lh, p, translucent ? BackdropDrawn : Opaque, r, track, false);
                g.Restore(state);
                // Thin outline, like the Windows 11 window border.
                if (!p.Band)
                    using (Pen pen = new Pen(p.Dark ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(50, 0, 0, 0)))
                        g.DrawPath(pen, win);
            }
        }

        static bool IsSettling(Reading cur, DeviceTrack track)
        {
            return cur.State == MouseState.Ok && !cur.Charging && track != null && cur.Time < track.SettleUntil;
        }

        static string StateText(Reading r, DeviceTrack track, bool settling)
        {
            switch (r.State)
            {
                case MouseState.Ok:
                    if (r.Charging) return Lang.T(r.Full ? "State.Charged" : "State.Charging");
                    if (settling) return Lang.T("State.Settling");
                    return Lang.T("State.OnBattery");
                case MouseState.Asleep: return Lang.T("State.Asleep");
                case MouseState.Error: return Lang.T("State.Error");
                default: return Lang.T(track != null ? "State.NotDetected" : "State.NoMouse");
            }
        }

        // Text with an optional halo, so it stays readable over glass.
        static void DrawText(Graphics g, string text, Font f, Color c, RectangleF r, StringFormat fmt, Palette p, bool halo, float s)
        {
            if (halo)
            {
                Color h = p.Dark ? Color.FromArgb(90, 0, 0, 0) : Color.FromArgb(110, 255, 255, 255);
                using (SolidBrush hb = new SolidBrush(h))
                {
                    float d = Math.Max(1f, s);
                    if (p.Gloss)
                    {
                        // Aero: glow all around the text.
                        g.DrawString(text, f, hb, new RectangleF(r.X - d, r.Y, r.Width, r.Height), fmt);
                        g.DrawString(text, f, hb, new RectangleF(r.X + d, r.Y, r.Width, r.Height), fmt);
                        g.DrawString(text, f, hb, new RectangleF(r.X, r.Y - d, r.Width, r.Height), fmt);
                    }
                    g.DrawString(text, f, hb, new RectangleF(r.X, r.Y + d, r.Width, r.Height), fmt);
                }
            }
            using (SolidBrush b = new SolidBrush(c)) g.DrawString(text, f, b, r, fmt);
        }

        // Draws the whole window. W, H: size in pixels. mode: Opaque (style background),
        // LiveAcrylic (Windows blurs behind: background left fully transparent),
        // BackdropDrawn (the caller drew the blurred backdrop: only the tint is added).
        public static void PaintContent(Graphics g, float W, float H, Palette p, int mode, Reading cur, DeviceTrack track, bool gearHot)
        {
            float s = g.DpiX / 96f;
            bool glass = mode != Opaque;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // ---- Background ----
            if (mode == LiveAcrylic)
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.Clear(Color.FromArgb(0, 0, 0, 0));
                g.CompositingMode = CompositingMode.SourceOver;
            }
            else if (mode == BackdropDrawn)
                using (SolidBrush b = new SolidBrush(p.Tint)) g.FillRectangle(b, 0, 0, W, H);
            else
                using (SolidBrush b = new SolidBrush(p.Bg)) g.FillRectangle(b, 0, 0, W, H);

            if (p.Shine)
            {
                // Liquid Glass: light falling from the top, and a bright rim.
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(0, -1, W, H * 0.55f + 2),
                           Color.FromArgb(p.Dark ? 46 : 110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(lb, 0, 0, W, H * 0.55f);
                using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(0, H * 0.7f, W, H * 0.3f + 1),
                           Color.FromArgb(0, 255, 255, 255), Color.FromArgb(p.Dark ? 18 : 40, 255, 255, 255), 90f))
                    g.FillRectangle(lb, 0, H * 0.7f, W, H * 0.3f);
                float edge = Math.Max(1f, 1.2f * s);
                RectangleF er = new RectangleF(edge / 2f, edge / 2f, W - edge, H - edge);
                using (GraphicsPath ep = Ui.RoundRect(er, 8 * s))
                using (LinearGradientBrush eb = new LinearGradientBrush(new RectangleF(0, 0, W, H),
                           Color.FromArgb(p.Dark ? 150 : 230, 255, 255, 255), Color.FromArgb(p.Dark ? 30 : 90, 255, 255, 255), 60f))
                using (Pen pen = new Pen(eb, edge))
                    g.DrawPath(pen, ep);
            }
            if (p.Gloss)
            {
                // Aero: diagonal reflection on the upper part of the glass, dark frame with a light inner line.
                using (GraphicsPath refl = new GraphicsPath())
                {
                    refl.AddLine(0, 0, W, 0);
                    refl.AddLine(W, 0, W, H * 0.16f);
                    refl.AddBezier(W, H * 0.16f, W * 0.65f, H * 0.22f, W * 0.4f, H * 0.42f, 0, H * 0.46f);
                    refl.CloseFigure();
                    using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(0, -1, W, H * 0.46f + 2),
                               Color.FromArgb(p.Dark ? 60 : 120, 255, 255, 255), Color.FromArgb(p.Dark ? 10 : 30, 255, 255, 255), 75f))
                        g.FillPath(lb, refl);
                }
                using (Pen outer = new Pen(Color.FromArgb(p.Dark ? 170 : 130, 20, 40, 70)))
                    g.DrawRectangle(outer, 0, 0, W - 1, H - 1);
                using (Pen inner = new Pen(Color.FromArgb(p.Dark ? 60 : 150, 255, 255, 255)))
                    g.DrawRectangle(inner, 1, 1, W - 3, H - 3);
            }
            if (p.Band)
                using (SolidBrush b = new SolidBrush(p.Accent)) g.FillRectangle(b, 0, 0, W, 40 * s);
            if (p.Style == "classic" || (p.Style == "metro" && !p.Band))
                using (Pen pen = new Pen(p.Border)) g.DrawRectangle(pen, 0, 0, W - 1, H - 1);
            else if (p.Style == "metro")
                using (Pen pen = new Pen(p.Accent)) g.DrawRectangle(pen, 0, 0, W - 1, H - 1);

            g.TextRenderingHint = mode == LiveAcrylic ? TextRenderingHint.AntiAliasGridFit : TextRenderingHint.ClearTypeGridFit;
            bool halo = glass && p.TextShadow;

            bool ok = cur.State == MouseState.Ok;
            bool charging = ok && cur.Charging;
            bool settling = IsSettling(cur, track);
            bool live = ok && !charging && !settling;                       // percentage is trustworthy and current
            Reading lastTrusted = track != null ? track.LastTrusted : null;
            Reading lastOk = track != null ? track.LastOk : null;
            Reading shown = live ? cur : lastTrusted;                       // source of the displayed percentage
            Reading volt = ok ? cur : lastOk;
            float pad = 20 * s;
            float w = W - 2 * pad;
            Color fg = p.Fg, sub = glass ? Theme.Mix(p.Sub, p.Fg, 0.4f) : p.Sub;   // more contrast over glass
            string title = cur.Name.Length > 0 ? cur.Name : track != null ? track.Name : Lang.T("NoMouse");

            using (Font fTitle = Theme.MakeFont(p.TitleFont, p.TitleSize))
            using (Font fBig = Theme.MakeFont(p.BigFont, p.BigSize))
            using (Font fMid = Theme.MakeFont(p.BigFont, 22f))
            using (Font fRow = Theme.MakeFont(p.BodyFont, 9.5f))
            using (StringFormat right = new StringFormat())
            using (StringFormat left = new StringFormat())
            using (StringFormat titleFmt = new StringFormat())
            {
                right.Alignment = StringAlignment.Far;
                right.FormatFlags |= StringFormatFlags.NoWrap;
                left.FormatFlags |= StringFormatFlags.NoWrap;
                titleFmt.FormatFlags |= StringFormatFlags.NoWrap;
                titleFmt.Trimming = StringTrimming.EllipsisCharacter;

                // ---- Title and gear ----
                RectangleF gearR = GearRect(W, s, p);
                if (p.Band)
                {
                    DrawText(g, title, fTitle, Color.White, new RectangleF(pad - 3 * s, 8 * s, w - 26 * s, 26 * s), titleFmt, p, false, s);
                }
                else DrawText(g, title, fTitle, fg, new RectangleF(pad - 2 * s, 14 * s, w - 26 * s, 24 * s), titleFmt, p, halo, s);
                float inset = gearR.Width * 0.08f;
                using (GraphicsPath gp = Ui.Gear(new RectangleF(gearR.X + inset, gearR.Y + inset, gearR.Width - 2 * inset, gearR.Height - 2 * inset), 8))
                using (SolidBrush gb = new SolidBrush(p.Band ? Color.FromArgb(gearHot ? 255 : 200, 255, 255, 255) : gearHot ? fg : sub))
                    g.FillPath(gb, gp);

                // ---- Level bar ----
                float bh = p.BarHeight * s, br = p.BarRadius * s;
                float by = 104 * s - bh / 2f;
                RectangleF bar = new RectangleF(pad, by, w, bh);
                Color trackColor = glass ? (p.Dark ? Color.FromArgb(56, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0)) : p.Track;
                using (GraphicsPath pt = Ui.RoundRect(bar, br))
                using (SolidBrush bt = new SolidBrush(trackColor))
                    g.FillPath(bt, pt);

                float fillWidth = 0;
                Color fillColor = Color.Empty;
                if (charging)
                {
                    // Charging: show a bolt instead of the percentage (overestimated on voltage-based mice).
                    bool full = cur.Full;
                    fillColor = full ? Ui.LevelColor(100, false) : Ui.ChargeBlue;
                    using (SolidBrush bb = new SolidBrush(fillColor))
                        g.FillPolygon(bb, Ui.Bolt(new RectangleF(pad, 44 * s, 24 * s, 40 * s)));
                    fillWidth = cur.VoltageBased || full ? w : Math.Max(8 * s, w * cur.Percent / 100f);
                    DrawText(g, Lang.T(full ? "Big.Charged" : "Big.Charging"), fMid, fg, new RectangleF(pad + 28 * s, 44 * s, w, 50 * s), left, p, halo, s);
                }
                else if (settling)
                {
                    DrawText(g, Lang.T("Big.Settling"), fMid, sub, new RectangleF(pad - 6 * s, 44 * s, w, 50 * s), left, p, halo, s);
                    int min = Math.Max(1, (int)Math.Ceiling((track.SettleUntil - DateTime.Now).TotalMinutes));
                    DrawText(g, Lang.F("MinLeft", min), fRow, sub, new RectangleF(pad, 64 * s, w, 20 * s), right, p, halo, s);
                }
                else
                {
                    string big = shown != null ? Lang.Pct(shown.Percent) : Lang.Pct("—");
                    float bigY = (p.BigSize > 32f ? 32 : 34) * s;
                    DrawText(g, big, fBig, live ? fg : sub, new RectangleF(pad - 8 * s, bigY, w + 8 * s, 70 * s), left, p, halo, s);
                    string side = live ? StateText(cur, track, false) : (shown != null ? Lang.T("LastKnown") : "");
                    DrawText(g, side, fRow, sub, new RectangleF(pad, 64 * s, w, 20 * s), right, p, halo, s);
                    if (shown != null && shown.Percent > 0)
                    {
                        fillWidth = Math.Max(8 * s, w * shown.Percent / 100f);
                        fillColor = live ? Ui.LevelColor(shown.Percent, false) : sub;
                    }
                }
                if (fillWidth > 0)
                {
                    RectangleF fr = new RectangleF(pad, by, Math.Max(fillWidth, 2 * br), bh);
                    using (GraphicsPath pf = Ui.RoundRect(fr, br))
                    {
                        using (SolidBrush bf = new SolidBrush(fillColor)) g.FillPath(bf, pf);
                        if (p.Gloss || p.Shine)
                        {
                            // Glossy bar: light upper half (Aero progress bar, glass highlight).
                            RectangleF top = new RectangleF(fr.X, fr.Y, fr.Width, fr.Height / 2f);
                            Region old = g.Clip;
                            g.SetClip(pf, CombineMode.Intersect);
                            using (LinearGradientBrush lb = new LinearGradientBrush(new RectangleF(top.X, top.Y - 1, top.Width, top.Height + 2),
                                       Color.FromArgb(p.Gloss ? 150 : 110, 255, 255, 255), Color.FromArgb(p.Gloss ? 40 : 0, 255, 255, 255), 90f))
                                g.FillRectangle(lb, top);
                            g.Clip = old;
                        }
                    }
                }

                // ---- Rows ----
                string level = (cur.VoltageBased && !cur.Full ? "≈ " : "") + Lang.Pct(cur.Percent);
                string connection = cur.Connection.Length > 0 ? cur.Connection
                                  : (lastOk != null && cur.State != MouseState.NoDevice ? lastOk.Connection : "");
                string[][] rows = new string[][] {
                    new string[] { Lang.T("Row.Status"), StateText(cur, track, settling) },
                    new string[] { Lang.T("Row.Voltage"), volt != null && volt.Millivolts > 0 ? (volt.Millivolts / 1000.0).ToString("0.00", Lang.Culture) + " V" : "—" },
                    new string[] { Lang.T("Row.Connection"), Lang.Connection(connection) },
                    // Row shown only while charging: level reported by the mouse.
                    new string[] { charging ? Lang.T(cur.VoltageBased ? "Row.ReportedLevel" : "Row.Level") : null, level },
                    new string[] { Lang.T("Row.LastReading"), lastOk != null ? lastOk.Time.ToString("HH:mm:ss") : "—" },
                };
                int count = 0;
                foreach (string[] row in rows) if (row[0] != null) count++;

                if (p.CardRows)
                {
                    // Material: rows on a raised card (soft shadow = elevation).
                    RectangleF card = new RectangleF(pad - 10 * s, 117 * s, w + 20 * s, count * 25 * s + 9 * s);
                    for (int i = 3; i >= 1; i--)
                        using (GraphicsPath sh = Ui.RoundRect(new RectangleF(card.X, card.Y + i * 0.7f * s, card.Width, card.Height), 12 * s))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(p.Dark ? 30 : 14, 0, 0, 0)))
                            g.FillPath(b, sh);
                    using (GraphicsPath cp = Ui.RoundRect(card, 12 * s))
                    using (SolidBrush b = new SolidBrush(p.Surface))
                        g.FillPath(b, cp);
                }

                float y = 124 * s;
                foreach (string[] row in rows)
                {
                    if (row[0] == null) continue;
                    DrawText(g, row[0], fRow, sub, new RectangleF(pad - 2 * s, y, w, 22 * s), left, p, halo, s);
                    DrawText(g, row[1], fRow, fg, new RectangleF(pad, y, w + 2 * s, 22 * s), right, p, halo, s);
                    y += 25 * s;
                }
            }
        }
    }
}
