using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    class MainForm : Form
    {
        readonly TrayContext ctx;
        readonly Panel sidebar, host;
        readonly List<FlatButton> nav = new List<FlatButton>();
        readonly List<Page> pages = new List<Page>();
        readonly Label sideStatus, sideToday;
        readonly Bitmap logo;
        int current;

        public MainForm(TrayContext ctx)
        {
            this.ctx = ctx;
            Text = "Fokkus";
            Icon = IconFactory.AppIcon(32);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(10);
            KeyPreview = true;
            ClientSize = new Size(Theme.S(960), Theme.S(660));
            logo = IconFactory.RenderLogo(Theme.S(44));

            SuspendLayout();
            sidebar = new DoubleBufferedPanel();
            sidebar.BackColor = Theme.Sidebar;
            sidebar.SetBounds(0, 0, Theme.S(220), ClientSize.Height);
            sidebar.Paint += PaintSidebar;
            Controls.Add(sidebar);

            host = new DoubleBufferedPanel();
            host.BackColor = Theme.Bg;
            host.SetBounds(Theme.S(220), 0, ClientSize.Width - Theme.S(220), ClientSize.Height);
            Controls.Add(host);

            string[] names = { "Timer", "Plan", "Statistics", "Websites", "Apps", "Tips", "Settings" };
            string[] glyphs = { Glyph.Timer, Glyph.Calendar, Glyph.Chart, Glyph.Globe, Glyph.Apps, Glyph.Bulb, Glyph.Settings };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                var b = new FlatButton(names[i]);
                b.Glyph = glyphs[i];
                b.LeftAlign = true;
                b.BaseColor = Color.Transparent;
                b.HoverColor = Theme.Surface;
                b.SelectedColor = Theme.Surface2;
                b.Font = Theme.F(10.5f);
                b.SetBounds(Theme.S(12), Theme.S(96) + i * Theme.S(44), Theme.S(196), Theme.S(38));
                b.Click += (s, e) => Navigate(index);
                sidebar.Controls.Add(b);
                nav.Add(b);
            }

            sideStatus = MakeSideLabel(ClientSize.Height - Theme.S(86), Theme.FB(9.5f), Theme.Text);
            sideToday = MakeSideLabel(ClientSize.Height - Theme.S(60), Theme.F(9), Theme.Muted);

            Size pageSize = host.ClientSize;
            pages.Add(new TimerPage(ctx, pageSize));
            pages.Add(new PlanPage(ctx, pageSize));
            pages.Add(new StatsPage(ctx, pageSize));
            pages.Add(new WebsitesPage(ctx, pageSize));
            pages.Add(new AppsPage(ctx, pageSize));
            pages.Add(new TipsPage(ctx, pageSize));
            pages.Add(new SettingsPage(ctx, pageSize));
            foreach (var p in pages)
            {
                p.Location = Point.Empty;
                p.Visible = false;
                host.Controls.Add(p);
            }
            ResumeLayout(false);
            Navigate(0);
        }

        Label MakeSideLabel(int y, Font f, Color c)
        {
            var l = new Label();
            l.AutoSize = false;
            l.BackColor = Theme.Sidebar;
            l.ForeColor = c;
            l.Font = f;
            l.UseMnemonic = false;
            l.SetBounds(Theme.S(24), y, Theme.S(186), Theme.S(22));
            sidebar.Controls.Add(l);
            return l;
        }

        void PaintSidebar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.DrawImage(logo, Theme.S(18), Theme.S(22));
            Theme.DrawText(g, "Fokkus", Theme.FB(14), Theme.Text, new Rectangle(Theme.S(72), Theme.S(22), Theme.S(140), Theme.S(28)), Theme.Left);
            Theme.DrawText(g, "stay locked in", Theme.F(8.5f), Theme.Muted, new Rectangle(Theme.S(72), Theme.S(48), Theme.S(140), Theme.S(18)), Theme.Left);
            using (var p = new Pen(Theme.Surface2)) g.DrawLine(p, Theme.S(20), ClientSize.Height - Theme.S(100), Theme.S(200), ClientSize.Height - Theme.S(100));
        }

        public void Navigate(int index)
        {
            current = index;
            for (int i = 0; i < pages.Count; i++)
            {
                nav[i].SetSelected(i == index);
                if (i == index) { pages[i].RefreshView(); pages[i].Visible = true; pages[i].BringToFront(); }
                else pages[i].Visible = false;
            }
        }

        public Bitmap Snapshot()
        {
            var bmp = new Bitmap(ClientSize.Width, ClientSize.Height);
            sidebar.DrawToBitmap(bmp, sidebar.Bounds);
            host.DrawToBitmap(bmp, host.Bounds);
            return bmp;
        }

        public void RefreshAll()
        {
            foreach (var p in pages) p.RefreshView();
            UpdateSidebar();
        }

        public void RefreshPage<T>() where T : Page
        {
            foreach (var p in pages) if (p is T && p.Visible) p.RefreshView();
        }

        public T Page<T>() where T : Page
        {
            return pages.OfType<T>().FirstOrDefault();
        }

        public void OnTick()
        {
            if (!Visible || WindowState == FormWindowState.Minimized) return;
            pages[current].OnTick();
            UpdateSidebar();
        }

        void UpdateSidebar()
        {
            var eng = ctx.Engine;
            string status;
            Color c;
            if (eng.State == RunState.Idle) { status = "●  Not running"; c = Theme.Muted; }
            else if (eng.IsLockedIn) { status = "●  Locked in · " + Theme.FormatTime(eng.Remaining); c = Theme.Focus; }
            else { status = "●  " + Theme.PhaseName(eng.Phase) + " · " + Theme.FormatTime(eng.Remaining); c = Theme.PhaseColor(eng.Phase); }
            if (eng.State == RunState.Paused) status += " (paused)";
            if (sideStatus.Text != status) sideStatus.Text = status;
            sideStatus.ForeColor = c;
            int pomos = ctx.Sessions.PomodorosOn(DateTime.Today, null);
            string today = "Today: " + pomos + (pomos == 1 ? " pomodoro" : " pomodoros") + " · " + Theme.Compact(ctx.Sessions.MinutesOn(DateTime.Today));
            if (sideToday.Text != today) sideToday.Text = today;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Space && current == 0 && !(ActiveControl is TextBox))
            {
                ctx.ToggleTimer();
                return true;
            }
            if (keyData == (Keys.Control | Keys.W)) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && ctx.Settings.CloseToTray)
            {
                Hide();
                WindowState = FormWindowState.Normal;
                ctx.OnMainHidden();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !ctx.IsExiting)
            {
                e.Cancel = true;
                if (ctx.Settings.CloseToTray)
                {
                    Hide();
                    ctx.OnMainHidden();
                }
                else ctx.Exit();
            }
            base.OnFormClosing(e);
        }
    }

    class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        }
    }
}
