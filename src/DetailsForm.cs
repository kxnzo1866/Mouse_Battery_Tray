using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseBatteryTray
{
    // ------------------------------------------------------------------
    //  Details window (left click on the icon)
    // ------------------------------------------------------------------
    class DetailsForm : Form
    {
        public Reading Current = new Reading();
        public DeviceTrack Track;     // history of the displayed mouse (may be null)
        public DateTime HiddenAt = DateTime.MinValue;
        public event EventHandler SettingsRequested;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out RECT r);

        readonly Label gear = new Label();
        readonly ToolTip gearTip = new ToolTip();
        bool gearHot;

        public DetailsForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            // Gear in the top-right corner: opens the settings window.
            gear.AutoSize = false;
            gear.Cursor = Cursors.Hand;
            gear.Paint += delegate(object sender, PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float inset = gear.Width * 0.08f;
                using (GraphicsPath p = Ui.Gear(new RectangleF(inset, inset, gear.Width - 2 * inset, gear.Height - 2 * inset), 8))
                using (SolidBrush b = new SolidBrush(gearHot ? Theme.Fg : Theme.Sub))
                    g.FillPath(b, p);
            };
            gear.MouseEnter += delegate { gearHot = true; gear.Invalidate(); };
            gear.MouseLeave += delegate { gearHot = false; gear.Invalidate(); };
            gear.Click += delegate { if (SettingsRequested != null) SettingsRequested(this, EventArgs.Empty); };
            Controls.Add(gear);

            ApplyTexts();
            Lang.Changed += OnLangChanged;
            Theme.Changed += OnThemeChanged;
        }

        void ApplyTexts()
        {
            gearTip.SetToolTip(gear, Lang.T("Gear.Tip"));
        }

        void OnLangChanged(object sender, EventArgs e)
        {
            ApplyTexts();
            if (Visible) Relayout();
        }

        void OnThemeChanged(object sender, EventArgs e)
        {
            gear.BackColor = Theme.Bg;
            Invalidate(true);
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

        string Title
        {
            get
            {
                if (Current.Name.Length > 0) return Current.Name;
                return Track != null ? Track.Name : Lang.T("NoMouse");
            }
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
            bool tall = Current.State == MouseState.Ok && Current.Charging;
            Size = new Size((int)(310 * s), (int)((tall ? 262 : 237) * s));
            int gsz = (int)(20 * s);
            gear.SetBounds(Width - (int)(18 * s) - gsz, (int)(13 * s), gsz, gsz);
            gear.BackColor = Theme.Bg;
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

        public void PaintTo(Graphics g) { OnPaint(new PaintEventArgs(g, ClientRectangle)); }

        bool Settling
        {
            get { return Current.State == MouseState.Ok && !Current.Charging && Track != null && Current.Time < Track.SettleUntil; }
        }

        string StateText(Reading r)
        {
            switch (r.State)
            {
                case MouseState.Ok:
                    if (r.Charging) return Lang.T(r.Full ? "State.Charged" : "State.Charging");
                    if (Settling) return Lang.T("State.Settling");
                    return Lang.T("State.OnBattery");
                case MouseState.Asleep: return Lang.T("State.Asleep");
                case MouseState.Error: return Lang.T("State.Error");
                default: return Lang.T(Track != null ? "State.NotDetected" : "State.NoMouse");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            Color bg = Theme.Bg, fg = Theme.Fg, sub = Theme.Sub, track = Theme.Track, border = Theme.Border;

            g.Clear(bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (Pen p = new Pen(border)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);

            bool ok = Current.State == MouseState.Ok;
            bool charging = ok && Current.Charging;
            bool settling = Settling;
            bool live = ok && !charging && !settling;                       // percentage is trustworthy and current
            Reading lastTrusted = Track != null ? Track.LastTrusted : null;
            Reading lastOk = Track != null ? Track.LastOk : null;
            Reading shown = live ? Current : lastTrusted;                   // source of the displayed percentage
            Reading volt = ok ? Current : lastOk;
            float pad = 20 * s;
            float w = ClientSize.Width - 2 * pad;

            using (SolidBrush bFg = new SolidBrush(fg))
            using (SolidBrush bSub = new SolidBrush(sub))
            using (Font fTitle = new Font("Segoe UI Semibold", 10.5f))
            using (Font fBig = new Font("Segoe UI Light", 32f))
            using (Font fRow = new Font("Segoe UI", 9.5f))
            using (StringFormat right = new StringFormat())
            using (StringFormat titleFmt = new StringFormat())
            {
                right.Alignment = StringAlignment.Far;
                right.FormatFlags |= StringFormatFlags.NoWrap;
                titleFmt.FormatFlags |= StringFormatFlags.NoWrap;
                titleFmt.Trimming = StringTrimming.EllipsisCharacter;

                g.DrawString(Title, fTitle, bFg, new RectangleF(pad - 2 * s, 14 * s, w - 26 * s, 22 * s), titleFmt);

                RectangleF bar = new RectangleF(pad, 100 * s, w, 8 * s);
                using (GraphicsPath pt = Ui.RoundRect(bar, 4 * s))
                using (SolidBrush bt = new SolidBrush(track))
                    g.FillPath(bt, pt);

                if (charging)
                {
                    // Charging: show a bolt instead of the percentage (overestimated on voltage-based mice).
                    bool full = Current.Full;
                    using (SolidBrush bb = new SolidBrush(full ? Ui.LevelColor(100, false) : Ui.ChargeBlue))
                    {
                        g.FillPolygon(bb, Ui.Bolt(new RectangleF(pad, 44 * s, 24 * s, 40 * s)));
                        float fw = Current.VoltageBased || full ? w : Math.Max(8 * s, w * Current.Percent / 100f);
                        using (GraphicsPath pf = Ui.RoundRect(new RectangleF(pad, 100 * s, fw, 8 * s), 4 * s)) g.FillPath(bb, pf);
                    }
                    using (Font fMid = new Font("Segoe UI Light", 22f))
                        g.DrawString(Lang.T(full ? "Big.Charged" : "Big.Charging"), fMid, bFg, pad + 28 * s, 44 * s);
                }
                else if (settling)
                {
                    using (Font fMid = new Font("Segoe UI Light", 22f))
                        g.DrawString(Lang.T("Big.Settling"), fMid, bSub, pad - 6 * s, 44 * s);
                    int min = Math.Max(1, (int)Math.Ceiling((Track.SettleUntil - DateTime.Now).TotalMinutes));
                    g.DrawString(Lang.F("MinLeft", min), fRow, bSub, new RectangleF(pad, 64 * s, w, 20 * s), right);
                }
                else
                {
                    string big = shown != null ? Lang.Pct(shown.Percent) : Lang.Pct("—");
                    g.DrawString(big, fBig, live ? bFg : bSub, pad - 8 * s, 34 * s);
                    string side = live ? StateText(Current) : (shown != null ? Lang.T("LastKnown") : "");
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

                string level = (Current.VoltageBased && !Current.Full ? "≈ " : "") + Lang.Pct(Current.Percent);
                string connection = Current.Connection.Length > 0 ? Current.Connection
                                  : (lastOk != null && Current.State != MouseState.NoDevice ? lastOk.Connection : "");
                string[][] rows = new string[][] {
                    new string[] { Lang.T("Row.Status"), StateText(Current) },
                    new string[] { Lang.T("Row.Voltage"), volt != null && volt.Millivolts > 0 ? (volt.Millivolts / 1000.0).ToString("0.00", Lang.Culture) + " V" : "—" },
                    new string[] { Lang.T("Row.Connection"), Lang.Connection(connection) },
                    // Row shown only while charging: level reported by the mouse.
                    new string[] { charging ? Lang.T(Current.VoltageBased ? "Row.ReportedLevel" : "Row.Level") : null, level },
                    new string[] { Lang.T("Row.LastReading"), lastOk != null ? lastOk.Time.ToString("HH:mm:ss") : "—" },
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
}
