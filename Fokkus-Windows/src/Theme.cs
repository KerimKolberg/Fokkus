using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace Fokkus
{
    static class Theme
    {
        // Navy + teal, taken from the Fokkus logo.
        public static readonly Color Bg = Color.FromArgb(14, 27, 48);          // #0E1B30
        public static readonly Color Sidebar = Color.FromArgb(16, 32, 56);     // #102038 (logo navy)
        public static readonly Color Surface = Color.FromArgb(22, 42, 71);     // #162A47
        public static readonly Color Surface2 = Color.FromArgb(30, 53, 86);    // #1E3556
        public static readonly Color Hover = Color.FromArgb(40, 67, 102);      // #284366
        public static readonly Color Border = Color.FromArgb(47, 75, 114);     // #2F4B72
        public static readonly Color Text = Color.FromArgb(234, 241, 246);
        public static readonly Color Muted = Color.FromArgb(143, 163, 189);
        public static readonly Color Focus = Color.FromArgb(60, 188, 185);     // #3CBCB9 (logo teal)
        public static readonly Color ShortBreak = Color.FromArgb(242, 184, 75);
        public static readonly Color LongBreak = Color.FromArgb(167, 139, 250);
        public static readonly Color Warn = Color.FromArgb(255, 138, 128);
        public static readonly Color Idle = Color.FromArgb(111, 132, 160);

        // Chart chrome (charts are drawn directly on Bg).
        public static readonly Color GridLine = Color.FromArgb(29, 46, 74);
        public static readonly Color Baseline = Color.FromArgb(52, 78, 115);

        // Subject colours: validated categorical palette (dark steps), fixed order, never cycled.
        // All eight pass lightness, chroma, CVD (adjacent) and >= 3:1 contrast against Bg.
        public static readonly Color[] Series =
        {
            Color.FromArgb(0x39, 0x87, 0xE5), Color.FromArgb(0xD9, 0x59, 0x26), Color.FromArgb(0x19, 0x9E, 0x70),
            Color.FromArgb(0xC9, 0x85, 0x00), Color.FromArgb(0xD5, 0x51, 0x81), Color.FromArgb(0x00, 0x83, 0x00),
            Color.FromArgb(0x90, 0x85, 0xE9), Color.FromArgb(0xE6, 0x67, 0x67),
        };
        public static readonly Color SeriesOther = Color.FromArgb(111, 132, 160);

        public static Color SubjectColor(Subject s)
        {
            return s == null || s.ColorIndex < 0 || s.ColorIndex >= Series.Length ? SeriesOther : Series[s.ColorIndex];
        }

        public static float Scale = 1f;
        public static int S(float px) { return (int)Math.Round(px * Scale); }

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        static string iconFont;

        public static Font F(float pt) { return Get("Segoe UI", pt); }
        public static Font FB(float pt) { return Get("Segoe UI Semibold", pt); }
        public static Font FL(float pt) { return Get("Segoe UI Light", pt); }
        public static Font Icons(float pt) { return Get(IconFontName, pt); }
        public static Font Emoji(float pt) { return Get("Segoe UI Emoji", pt); }

        static Font Get(string name, float pt)
        {
            string key = name + "|" + pt;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(name, pt, FontStyle.Regular, GraphicsUnit.Point);
                fonts[key] = f;
            }
            return f;
        }

        public static string IconFontName
        {
            get
            {
                if (iconFont == null)
                {
                    iconFont = "Segoe MDL2 Assets";
                    using (var ifc = new InstalledFontCollection())
                        foreach (var ff in ifc.Families)
                            if (ff.Name == "Segoe Fluent Icons") { iconFont = ff.Name; break; }
                }
                return iconFont;
            }
        }

        public static Color PhaseColor(Phase p)
        {
            switch (p)
            {
                case Phase.ShortBreak: return ShortBreak;
                case Phase.LongBreak: return LongBreak;
                default: return Focus;
            }
        }

        public static string PhaseName(Phase p)
        {
            switch (p)
            {
                case Phase.ShortBreak: return "Short break";
                case Phase.LongBreak: return "Long break";
                default: return "Focus";
            }
        }

        public static Color Blend(Color a, Color b, float t)
        {
            if (t < 0) t = 0; if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t),
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, float t) { return Blend(c, Color.FromArgb(c.A, 255, 255, 255), t); }
        public static Color Darken(Color c, float t) { return Blend(c, Color.FromArgb(c.A, 0, 0, 0), t); }

        public static Color OnColor(Color c)
        {
            double l = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return l > 130 ? Color.FromArgb(11, 22, 40) : Color.White;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var p = RoundRect(r, radius))
            using (var b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawText(Graphics g, string text, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, f, r, c, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public const TextFormatFlags Center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
        public const TextFormatFlags Left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        public const TextFormatFlags Right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        public static string FormatTime(TimeSpan t)
        {
            int total = (int)Math.Ceiling(t.TotalSeconds - 0.001);
            if (total < 0) total = 0;
            int h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, s) : string.Format("{0:00}:{1:00}", m, s);
        }

        public static string FormatMinutes(int minutes)
        {
            if (minutes < 60) return minutes + " min";
            return string.Format("{0} h {1:00} min", minutes / 60, minutes % 60);
        }

        // Compact duration for tiles and axes: "45m", "3h 20m", "128h".
        public static string Compact(double minutes)
        {
            int m = (int)Math.Round(minutes);
            if (m < 60) return m + "m";
            if (m < 6000) return (m / 60) + "h" + (m % 60 > 0 ? " " + (m % 60) + "m" : "");
            return (m / 60) + "h";
        }
    }

    // Segoe Fluent Icons / Segoe MDL2 Assets code points.
    static class Glyph
    {
        public const string Play = "";
        public const string Pause = "";
        public const string Next = "";
        public const string Stop = "";
        public const string Settings = "";
        public const string Globe = "";
        public const string Apps = "";
        public const string Bulb = "";
        public const string Timer = "";
        public const string Lock = "";
        public const string Add = "";
        public const string Delete = "";
        public const string Close = "";
        public const string Shield = "";
        public const string Open = "";
        public const string Check = "";
        public const string Refresh = "";
        public const string Folder = "";
        public const string Minimize = "";
        public const string Calendar = "";
        public const string Chart = "";
        public const string Left = "";
        public const string Right = "";
        public const string Edit = "";
        public const string Down = "";
        public const string Up = "";
    }
}
