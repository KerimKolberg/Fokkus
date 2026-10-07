using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    // Small pop-up in the bottom-right corner that never steals keyboard focus.
    class TipToast : Form
    {
        string icon = "", title = "", body = "", actionText;
        Action action;
        Color accent = Theme.Focus;
        Rectangle actionRect, closeRect;
        bool hoverAction, hoverClose, fadingOut;
        readonly Timer life = new Timer();
        readonly Timer fade = new Timer();

        public TipToast()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            Text = "Fokkus";
            Opacity = 0;
            life.Tick += (s, e) => { life.Stop(); FadeOut(); };
            fade.Interval = 15;
            fade.Tick += OnFade;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.RoundCorners(Handle);
            Native.BorderColor(Handle, Theme.Border);
        }

        public void ShowToast(string icon, string title, string body, Color accent, int seconds, string actionText, Action action, Rectangle avoid)
        {
            Prepare(icon, title, body, accent, actionText, action, avoid);
            fadingOut = false;
            if (!Visible) { Opacity = 0; Show(); }
            fade.Start();
            Invalidate();
            life.Stop();
            life.Interval = Math.Max(2, seconds) * 1000;
            life.Start();
        }

        public void Prepare(string icon, string title, string body, Color accent, string actionText, Action action, Rectangle avoid)
        {
            this.icon = icon ?? "";
            this.title = title ?? "";
            this.body = body ?? "";
            this.accent = accent;
            this.actionText = actionText;
            this.action = action;

            int w = Theme.S(380);
            int textX = Theme.S(70), textW = w - textX - Theme.S(40);
            Size bodySize = TextRenderer.MeasureText(this.body, Theme.F(9.5f), new Size(textW, 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            int bodyBottom = Theme.S(42) + bodySize.Height;
            int h = Math.Max(Theme.S(78), bodyBottom + Theme.S(16));
            if (actionText != null)
            {
                actionRect = new Rectangle(textX, bodyBottom + Theme.S(10), TextRenderer.MeasureText(actionText, Theme.FB(9.5f)).Width + Theme.S(28), Theme.S(30));
                h = actionRect.Bottom + Theme.S(16);
            }
            else actionRect = Rectangle.Empty;
            closeRect = new Rectangle(w - Theme.S(34), Theme.S(10), Theme.S(24), Theme.S(24));

            Screen screen = avoid != Rectangle.Empty ? Screen.FromRectangle(avoid) : Screen.PrimaryScreen;
            Rectangle wa = screen.WorkingArea;
            var bounds = new Rectangle(wa.Right - w - Theme.S(16), wa.Bottom - h - Theme.S(16), w, h);
            if (avoid != Rectangle.Empty && bounds.IntersectsWith(avoid))
            {
                bounds.Y = avoid.Top - h - Theme.S(10);
                if (bounds.Y < wa.Top) bounds.Y = avoid.Bottom + Theme.S(10);
            }
            Bounds = bounds;
        }

        void FadeOut()
        {
            fadingOut = true;
            fade.Start();
        }

        void OnFade(object sender, EventArgs e)
        {
            if (fadingOut)
            {
                Opacity = Math.Max(0, Opacity - 0.08);
                if (Opacity <= 0.01) { fade.Stop(); Hide(); }
            }
            else
            {
                Opacity = Math.Min(0.98, Opacity + 0.12);
                if (Opacity >= 0.98) fade.Stop();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            using (var b = new SolidBrush(accent)) g.FillRectangle(b, 0, 0, Theme.S(4), Height);

            var circle = new RectangleF(Theme.S(18), Theme.S(16), Theme.S(40), Theme.S(40));
            using (var b = new SolidBrush(Color.FromArgb(48, accent))) g.FillEllipse(b, circle);
            Theme.DrawText(g, icon, Theme.Emoji(15), accent, Rectangle.Round(circle), Theme.Center);

            int textX = Theme.S(70), textW = Width - textX - Theme.S(40);
            Theme.DrawText(g, title, Theme.FB(11), Theme.Text, new Rectangle(textX, Theme.S(14), textW, Theme.S(24)), Theme.Left);
            Theme.DrawText(g, body, Theme.F(9.5f), Theme.Blend(Theme.Text, Theme.Muted, 0.4f),
                new Rectangle(textX, Theme.S(42), textW, Height - Theme.S(42)), TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top);

            if (hoverClose) Theme.FillRound(g, Theme.Hover, closeRect, Theme.S(6));
            Theme.DrawText(g, Glyph.Close, Theme.Icons(8), hoverClose ? Theme.Text : Theme.Muted, closeRect, Theme.Center);

            if (actionText != null)
            {
                Theme.FillRound(g, hoverAction ? Theme.Lighten(accent, 0.12f) : accent, actionRect, Theme.S(8));
                Theme.DrawText(g, actionText, Theme.FB(9.5f), Theme.OnColor(accent), actionRect, Theme.Center);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool ha = actionText != null && actionRect.Contains(e.Location), hc = closeRect.Contains(e.Location);
            if (ha != hoverAction || hc != hoverClose) { hoverAction = ha; hoverClose = hc; Invalidate(); }
            Cursor = ha || hc ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            life.Stop();
            if (fadingOut) { fadingOut = false; fade.Start(); }
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverAction = hoverClose = false;
            Invalidate();
            life.Stop();
            life.Interval = 4000;
            life.Start();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (actionText != null && actionRect.Contains(e.Location) && action != null)
            {
                var a = action;
                action = null;
                a();
            }
            life.Stop();
            FadeOut();
        }
    }
}
