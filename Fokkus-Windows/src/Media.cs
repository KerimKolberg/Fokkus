using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace Fokkus
{
    // Soft two-tone chime, synthesised in memory (no sound files needed).
    static class Chime
    {
        static byte[] wav;
        static SoundPlayer player;

        public static void Play()
        {
            try
            {
                if (wav == null) wav = Build();
                player = new SoundPlayer(new MemoryStream(wav));
                player.Play();
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        static byte[] Build()
        {
            const int rate = 44100;
            double length = 1.6;
            int n = (int)(rate * length);
            var samples = new double[n];
            AddNote(samples, rate, 659.25, 0.00, 1.1);
            AddNote(samples, rate, 987.77, 0.18, 1.3);
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' });
                w.Write(36 + n * 2);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' });
                w.Write(n * 2);
                for (int i = 0; i < n; i++)
                {
                    double v = Math.Max(-1, Math.Min(1, samples[i]));
                    w.Write((short)(v * 32767 * 0.32));
                }
                return ms.ToArray();
            }
        }

        static void AddNote(double[] buf, int rate, double freq, double start, double dur)
        {
            int s0 = (int)(start * rate), len = (int)(dur * rate);
            for (int i = 0; i < len && s0 + i < buf.Length; i++)
            {
                double t = i / (double)rate;
                double env = Math.Min(1, t / 0.008) * Math.Exp(-t * 3.2);
                buf[s0 + i] += env * (Math.Sin(2 * Math.PI * freq * t) + 0.18 * Math.Sin(4 * Math.PI * freq * t));
            }
        }
    }

    static class IconFactory
    {
        static Bitmap logo;
        static readonly Color LogoNavy = Color.FromArgb(16, 32, 56);

        // The Fokkus logo, embedded in the exe as a resource.
        static Bitmap Logo
        {
            get
            {
                if (logo == null)
                    try
                    {
                        using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Fokkus.logo.png"))
                            if (s != null) logo = new Bitmap(s);
                    }
                    catch { }
                return logo;
            }
        }

        // Round logo (the navy square is clipped to a circle).
        public static Bitmap RenderLogo(int size)
        {
            if (Logo == null) return RenderMark(size);
            // Scale first, then fill an anti-aliased circle with the scaled image.
            using (var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(Logo, new Rectangle(0, 0, size, size));
                }
                var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                using (var tb = new TextureBrush(scaled))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.FillEllipse(tb, 0, 0, size, size);
                }
                return bmp;
            }
        }

        // Simplified mark for tiny sizes: navy disc, teal progress ring, "K".
        public static Bitmap RenderMark(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (var b = new SolidBrush(LogoNavy)) g.FillEllipse(b, 0, 0, size - 0.5f, size - 0.5f);
                float th = Math.Max(1.4f, size * 0.085f), pad = th / 2 + size * 0.07f;
                var rr = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
                using (var p = new Pen(Color.FromArgb(70, Theme.Focus), th)) g.DrawEllipse(p, rr);
                using (var p = new Pen(Theme.Focus, th))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, rr, -90, 290);
                }
                using (var f = new Font("Segoe UI Semibold", size * 0.46f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (var b = new SolidBrush(Theme.Focus))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString("K", f, b, new RectangleF(0, size * 0.02f, size, size), sf);
            }
            return bmp;
        }

        public static Bitmap RenderAppIcon(int size)
        {
            return size >= 48 ? RenderLogo(size) : RenderMark(size);
        }

        static void DrawLock(Graphics g, float cx, float cy, float w, Color c)
        {
            float bh = w * 0.74f;
            var body = new RectangleF(cx - w / 2, cy - bh / 2 + w * 0.22f, w, bh);
            float sw = w * 0.62f, st = Math.Max(1f, w * 0.15f);
            float top = body.Top - sw * 0.78f;
            using (var pen = new Pen(c, st))
            using (var shackle = new GraphicsPath())
            {
                shackle.AddLine(cx - sw / 2, body.Top + 1, cx - sw / 2, top + sw / 2);
                shackle.AddArc(cx - sw / 2, top, sw, sw, 180, 180);
                shackle.AddLine(cx + sw / 2, top + sw / 2, cx + sw / 2, body.Top + 1);
                g.DrawPath(pen, shackle);
            }
            Theme.FillRound(g, c, body, w * 0.16f);
        }

        public static Icon AppIcon(int size)
        {
            using (var bmp = RenderAppIcon(size)) return ToIcon(bmp);
        }

        // Tray icon: a ring showing the remaining time in the phase colour, a lock while locked in.
        public static Icon TrayIcon(Color color, float remaining, bool locked, RunState state)
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    float th = Math.Max(2f, size * 0.15f);
                    var r = new RectangleF(th / 2 + 0.5f, th / 2 + 0.5f, size - th - 1, size - th - 1);
                    using (var p = new Pen(Color.FromArgb(state == RunState.Idle ? 0 : 90, 128, 134, 150), th)) g.DrawEllipse(p, r);
                    if (remaining > 0.01f)
                        using (var p = new Pen(color, th))
                        {
                            p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                            g.DrawArc(p, r, -90, 360 * Math.Min(1f, remaining));
                        }
                    if (locked || state == RunState.Idle)
                        DrawLock(g, size / 2f, size / 2f, size * 0.36f, Color.White);
                    else
                        using (var b = new SolidBrush(color))
                            g.FillEllipse(b, size / 2f - size * 0.13f, size / 2f - size * 0.13f, size * 0.26f, size * 0.26f);
                }
                return ToIcon(bmp);
            }
        }

        static Icon ToIcon(Bitmap bmp)
        {
            IntPtr h = bmp.GetHicon();
            try
            {
                using (var tmp = Icon.FromHandle(h)) return (Icon)tmp.Clone();
            }
            finally { Native.DestroyIcon(h); }
        }

        // Writes a multi-resolution .ico (PNG entries) – used once to create assets\fokkus.ico.
        public static void WriteIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = RenderAppIcon(s))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }

        public static void WritePng(string path, int size)
        {
            using (var bmp = RenderAppIcon(size)) bmp.Save(path, ImageFormat.Png);
        }
    }
}
