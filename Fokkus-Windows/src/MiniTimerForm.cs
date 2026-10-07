using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    // The tiny always-on-top timer. Drag to move, double-click to open the main window, right-click for the menu.
    class MiniTimerForm : Form
    {
        readonly TrayContext ctx;
        readonly Rectangle[] buttons = new Rectangle[3];
        bool hover, dragging, moved;
        Point downScreen, downLocation;
        int hoverBtn = -1, pressBtn = -1;
        string flash;
        DateTime flashUntil;
        string lastKey;

        public MiniTimerForm(TrayContext ctx)
        {
            this.ctx = ctx;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            Text = "Fokkus timer";
            ClientSize = new Size(Theme.S(236), Theme.S(62));
            ContextMenuStrip = ctx.BuildMenu();
            int b = Theme.S(28);
            for (int i = 0; i < 3; i++)
                buttons[i] = new Rectangle(ClientSize.Width - Theme.S(10) - (3 - i) * b - (2 - i) * Theme.S(2), (ClientSize.Height - b) / 2, b, b);
            ApplyOpacity();
            PlaceInitially();
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

        public void ApplyOpacity() { Opacity = ctx.Settings.MiniOpacity / 100.0; }

        void PlaceInitially()
        {
            var s = ctx.Settings;
            var r = new Rectangle(s.MiniX, s.MiniY, Width, Height);
            bool saved = !(s.MiniX == -1 && s.MiniY == -1);
            if (saved && Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(r)))
                Location = r.Location;
            else
            {
                var wa = Screen.PrimaryScreen.WorkingArea;
                Location = new Point(wa.Right - Width - Theme.S(20), wa.Bottom - Height - Theme.S(20));
            }
        }

        public void Flash(string text)
        {
            flash = text;
            flashUntil = DateTime.Now.AddSeconds(4);
            Invalidate();
        }

        public void RefreshView()
        {
            var eng = ctx.Engine;
            string key = Theme.FormatTime(eng.Remaining) + eng.State + eng.Phase + (flash != null && DateTime.Now < flashUntil) + ctx.StrictLocked + ShowMotto + ctx.Settings.ActiveSubjectId;
            if (key == lastKey) return;
            lastKey = key;
            Invalidate();
        }

        // For 10 of every 40 seconds the subtitle shows a short motivation line instead of the status.
        bool ShowMotto
        {
            get { return ctx.Engine.State == RunState.Running && (DateTime.Now.Second % 40) >= 30; }
        }

        string SubText()
        {
            var eng = ctx.Engine;
            string phase = Theme.PhaseName(eng.Phase).ToUpperInvariant();
            var subj = Planner.Active(ctx.Settings);
            switch (eng.State)
            {
                case RunState.Idle: return subj != null ? "READY · " + subj.Name.ToUpperInvariant() : "READY TO LOCK IN";
                case RunState.Paused: return phase + " · PAUSED";
                case RunState.Ready: return "NEXT: " + phase;
                default:
                    if (eng.Phase == Phase.Focus)
                    {
                        string n = Math.Min(eng.CompletedInCycle + 1, eng.SessionsPerCycle) + "/" + eng.SessionsPerCycle;
                        return subj != null ? subj.Name.ToUpperInvariant() + " · " + n : "FOCUS " + n + (eng.IsLockedIn ? " · LOCKED IN" : "");
                    }
                    return phase;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            var eng = ctx.Engine;
            Color pc = eng.State == RunState.Idle ? Theme.Idle : Theme.PhaseColor(eng.Phase);
            bool dim = eng.State == RunState.Paused || eng.State == RunState.Ready;

            int rs = Theme.S(40);
            float th = Theme.S(4);
            var rr = new RectangleF(Theme.S(12) + th / 2, (Height - rs) / 2f + th / 2, rs - th, rs - th);
            using (var p = new Pen(Theme.Surface2, th)) g.DrawEllipse(p, rr);
            float rem = eng.State == RunState.Idle ? 1f : (float)(1 - eng.Progress);
            if (rem > 0.002f)
                using (var p = new Pen(dim ? Theme.Blend(pc, Theme.Surface2, 0.45f) : pc, th))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                    g.DrawArc(p, rr, -90, 360 * rem);
                }
            var ringRect = new Rectangle(Theme.S(12), (Height - rs) / 2, rs, rs);
            if (eng.IsLockedIn) Theme.DrawText(g, Glyph.Lock, Theme.Icons(9.5f), pc, ringRect, Theme.Center);
            else if (eng.State == RunState.Running) using (var b = new SolidBrush(pc)) g.FillEllipse(b, ringRect.X + rs / 2f - Theme.S(4), ringRect.Y + rs / 2f - Theme.S(4), Theme.S(8), Theme.S(8));
            else Theme.DrawText(g, Glyph.Play, Theme.Icons(9.5f), pc, ringRect, Theme.Center);

            int tx = Theme.S(62), tw = buttons[0].Left - tx - Theme.S(4);
            Theme.DrawText(g, Theme.FormatTime(eng.Remaining), Theme.FB(16), Theme.Text, new Rectangle(tx, Theme.S(6), tw, Theme.S(32)), Theme.Left);
            bool flashing = flash != null && DateTime.Now < flashUntil;
            string sub = SubText();
            Color subColor = Theme.Muted;
            if (flashing) { sub = flash; subColor = Theme.Warn; }
            else if (ShowMotto)
            {
                int done, target;
                Planner.TodayTotals(ctx.Settings, ctx.Sessions, out done, out target);
                sub = Motivation.Current(eng, done, target);
                subColor = Theme.Blend(Theme.Focus, Theme.Text, 0.25f);
            }
            Theme.DrawText(g, sub, Theme.F(7.5f), subColor, new Rectangle(tx, Theme.S(37), tw + Theme.S(4), Theme.S(18)), Theme.Left);

            bool canPause = !(eng.State == RunState.Running && ctx.StrictLocked);
            string[] glyphs = { eng.State == RunState.Running ? Glyph.Pause : Glyph.Play, Glyph.Next, Glyph.Open };
            bool[] enabled = { canPause, eng.State != RunState.Idle && !ctx.StrictLocked, true };
            for (int i = 0; i < 3; i++)
            {
                if (hoverBtn == i && enabled[i]) Theme.FillRound(g, Theme.Hover, buttons[i], Theme.S(7));
                Color c = !enabled[i] ? Theme.Blend(Theme.Muted, Theme.Surface, 0.6f) : (hover ? Theme.Text : Theme.Blend(Theme.Muted, Theme.Surface, 0.25f));
                Theme.DrawText(g, glyphs[i], Theme.Icons(i == 0 ? 10 : 9), c, buttons[i], Theme.Center);
            }
        }

        int HitButton(Point p)
        {
            for (int i = 0; i < buttons.Length; i++) if (buttons[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var cur = Cursor.Position;
                int dx = cur.X - downScreen.X, dy = cur.Y - downScreen.Y;
                if (!moved && Math.Abs(dx) + Math.Abs(dy) > Theme.S(3)) moved = true;
                if (moved) Location = new Point(downLocation.X + dx, downLocation.Y + dy);
                return;
            }
            int hb = HitButton(e.Location);
            if (hb != hoverBtn) { hoverBtn = hb; Invalidate(); }
            Cursor = hb >= 0 ? Cursors.Hand : Cursors.SizeAll;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            pressBtn = HitButton(e.Location);
            if (pressBtn < 0)
            {
                dragging = true;
                moved = false;
                downScreen = Cursor.Position;
                downLocation = Location;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (dragging)
            {
                dragging = false;
                if (moved)
                {
                    ctx.Settings.MiniX = Left;
                    ctx.Settings.MiniY = Top;
                    ctx.Settings.Save();
                }
            }
            else if (pressBtn >= 0 && pressBtn == HitButton(e.Location))
            {
                if (pressBtn == 0) ctx.ToggleTimer();
                else if (pressBtn == 1) ctx.Skip();
                else ctx.ShowMain();
            }
            pressBtn = -1;
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && HitButton(e.Location) < 0) ctx.ShowMain();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; hoverBtn = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !ctx.IsExiting)
            {
                e.Cancel = true;
                Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
