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
    // ------------------------------------------------------------------
    //  Appearance
    // ------------------------------------------------------------------
    static class Ui
    {
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

        // Colourful sample wallpaper drawn behind the style previews (with sharp details, so the blur shows).
        public static void DrawWallpaper(Graphics g, Rectangle r)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (LinearGradientBrush lb = new LinearGradientBrush(r, Color.FromArgb(46, 70, 170), Color.FromArgb(226, 88, 118), 25f))
                g.FillRectangle(lb, r);
            Blob(g, r.X + r.Width * 0.18f, r.Y + r.Height * 0.85f, r.Height * 0.75f, Color.FromArgb(255, 196, 80));
            Blob(g, r.X + r.Width * 0.86f, r.Y + r.Height * 0.12f, r.Height * 0.65f, Color.FromArgb(40, 210, 190));
            float u = Math.Max(1f, r.Height / 60f);
            using (Pen pen = new Pen(Color.FromArgb(150, 255, 255, 255), u))
            {
                for (int i = 0; i < 4; i++)
                {
                    float d = r.Height * (0.35f + 0.22f * i);
                    g.DrawEllipse(pen, r.X + r.Width * 0.62f - d / 2f, r.Y + r.Height * 0.62f - d / 2f, d, d);
                }
                for (int i = 0; i < 7; i++)
                    g.DrawLine(pen, r.X + i * r.Width / 6f, r.Bottom, r.X + i * r.Width / 6f + r.Height * 0.5f, r.Y);
            }
        }

        static void Blob(Graphics g, float cx, float cy, float radius, Color c)
        {
            using (GraphicsPath p = new GraphicsPath())
            {
                p.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
                using (PathGradientBrush pb = new PathGradientBrush(p))
                {
                    pb.CenterColor = Color.FromArgb(230, c);
                    pb.SurroundColors = new Color[] { Color.FromArgb(0, c) };
                    g.FillPath(pb, p);
                }
            }
        }

        // Cheap blur: shrink, then enlarge with smooth interpolation.
        public static Bitmap Blur(Bitmap src, int factor)
        {
            int w = Math.Max(1, src.Width / factor), h = Math.Max(1, src.Height / factor);
            Bitmap result = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            using (Bitmap small = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(small))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(src, new Rectangle(0, 0, w, h));
                }
                using (Graphics g = Graphics.FromImage(result))
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);   // no dark fringe at the edges
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(small, new Rectangle(0, 0, src.Width, src.Height), 0, 0, w, h, GraphicsUnit.Pixel, ia);
                }
            }
            return result;
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

        // Gear (settings button), drawn as a vector shape so it stays sharp at every DPI scale.
        public static GraphicsPath Gear(RectangleF r, int teeth)
        {
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
            float outer = Math.Min(r.Width, r.Height) / 2f, inner = outer * 0.72f, hole = outer * 0.32f;
            List<PointF> pts = new List<PointF>();
            double step = Math.PI * 2 / teeth;
            for (int i = 0; i < teeth; i++)
            {
                double a = i * step - Math.PI / 2;
                double[] angles = { a - step * 0.30, a - step * 0.18, a + step * 0.18, a + step * 0.30, a + step * 0.5 };
                float[] radii = { inner, outer, outer, inner, inner };
                for (int k = 0; k < angles.Length; k++)
                    pts.Add(new PointF(cx + radii[k] * (float)Math.Cos(angles[k]), cy + radii[k] * (float)Math.Sin(angles[k])));
            }
            GraphicsPath p = new GraphicsPath(FillMode.Alternate);
            p.AddPolygon(pts.ToArray());
            p.AddEllipse(cx - hole, cy - hole, hole * 2, hole * 2);
            return p;
        }

        // Notification area icon: a battery whose fill follows the level.
        public static Icon MakeIcon(int percent, IconMode mode, int size, bool darkTaskbar)
        {
            return MakeIcon(percent, mode, size, darkTaskbar, false);
        }

        // showNumber: the percentage is drawn as digits (with a thin level bar below) instead of the battery.
        // Only used when a level is known (Normal / Dim); charging and "no level" keep the battery drawing.
        public static Icon MakeIcon(int percent, IconMode mode, int size, bool darkTaskbar, bool showNumber)
        {
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    if (showNumber && (mode == IconMode.Normal || mode == IconMode.Dim))
                    {
                        DrawNumber(g, percent, mode == IconMode.Dim, size, darkTaskbar);
                        return ToIcon(bmp);
                    }
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
                return ToIcon(bmp);
            }
        }

        static Icon ToIcon(Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            try
            {
                using (Icon tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
            }
            finally { DestroyIcon(h); }
        }

        // Digits filling the top of the icon + a thin bar at the bottom showing the level.
        static void DrawNumber(Graphics g, int percent, bool dim, int size, bool darkTaskbar)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Color.Transparent);
            percent = Math.Max(0, Math.Min(100, percent));
            float u = size / 16f;
            Color line = darkTaskbar ? Color.White : Color.FromArgb(30, 30, 30);
            Color level = dim ? Color.FromArgb(140, 140, 140) : LevelColor(percent, false);
            // Digits in the taskbar colour (best contrast), red when the battery is low.
            Color text = dim ? Color.FromArgb(150, line) : percent < 20 ? level : line;

            float barH = Math.Max(2f, (float)Math.Round(2.5f * u));
            RectangleF digits = new RectangleF(0, 0, size, size - barH - Math.Max(1f, (float)Math.Round(u)));

            using (GraphicsPath p = new GraphicsPath())
            using (FontFamily ff = new FontFamily("Segoe UI"))
            {
                p.AddString(percent.ToString(), ff, (int)FontStyle.Bold, 100f, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF b = p.GetBounds();
                if (b.Width > 0 && b.Height > 0)
                {
                    // Full height; "100" is squeezed horizontally rather than shrunk.
                    float sy = digits.Height / b.Height;
                    float sx = Math.Min(sy, digits.Width / b.Width);
                    using (Matrix m = new Matrix())
                    {
                        m.Translate(digits.X + (digits.Width - b.Width * sx) / 2f, digits.Y);
                        m.Scale(sx, sy);
                        m.Translate(-b.X, -b.Y);
                        p.Transform(m);
                    }
                    using (SolidBrush tb = new SolidBrush(text)) g.FillPath(tb, p);
                }
            }

            RectangleF bar = new RectangleF(0, size - barH, size, barH);
            using (SolidBrush track = new SolidBrush(Color.FromArgb(70, line))) g.FillRectangle(track, bar);
            if (percent > 0)
                using (SolidBrush fill = new SolidBrush(level))
                    g.FillRectangle(fill, bar.X, bar.Y, Math.Max(u, bar.Width * percent / 100f), bar.Height);
        }
    }
}
