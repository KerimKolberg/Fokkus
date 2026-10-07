using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    abstract class Page : Panel
    {
        protected readonly TrayContext Ctx;
        protected Label Subtitle;
        protected Label StrictBanner;

        protected Page(TrayContext ctx, Size size)
        {
            Ctx = ctx;
            Size = size;
            BackColor = Theme.Bg;
            DoubleBuffered = true;
        }

        protected static int S(float v) { return Theme.S(v); }
        protected static int Pad { get { return Theme.S(32); } }

        protected Label AddLabel(string text, Font font, Color color, int x, int y, int w, int h, ContentAlignment align = ContentAlignment.TopLeft)
        {
            var l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.BackColor = Theme.Bg;
            l.AutoSize = false;
            l.TextAlign = align;
            l.UseMnemonic = false;
            l.SetBounds(x, y, w, h);
            Controls.Add(l);
            return l;
        }

        protected T Place<T>(T c, int x, int y, int w, int h) where T : Control
        {
            c.SetBounds(x, y, w, h);
            Controls.Add(c);
            return c;
        }

        protected void Header(string title, string subtitle)
        {
            AddLabel(title, Theme.FB(17), Theme.Text, Pad, S(24), Width - 2 * Pad, S(40));
            Subtitle = AddLabel(subtitle ?? "", Theme.F(9.5f), Theme.Muted, Pad, S(64), Width - 2 * Pad, S(22));
            StrictBanner = AddLabel(Glyph.Lock + "", Theme.FB(9), Theme.Warn, Width - Pad - S(300), S(34), S(300), S(22), ContentAlignment.MiddleRight);
            StrictBanner.Text = "Locked by strict mode until this focus ends";
            StrictBanner.Visible = false;
        }

        protected Label Section(string text, int x, int y, int w)
        {
            return AddLabel(text.ToUpperInvariant(), Theme.FB(8.5f), Theme.Muted, x, y, w, S(20));
        }

        protected FlatButton Button(string text, string glyph, int x, int y, int w, int h, EventHandler click)
        {
            var b = new FlatButton(text);
            b.Glyph = glyph;
            b.Click += click;
            return Place(b, x, y, w, h);
        }

        protected Toggle AddToggle(string text, string desc, int x, int y, int w, EventHandler changed)
        {
            var t = new Toggle(text, desc);
            t.CheckedChanged += changed;
            int h = Toggle.PreferredHeightFor(desc);
            if (desc != null)
            {
                int textH = TextRenderer.MeasureText(desc, Theme.F(8.5f), new Size(w - S(52), 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
                h = S(28) + textH + S(4);
            }
            return Place(t, x, y, w, h);
        }

        protected Stepper AddStepper(int x, int y, int w, int min, int max, int step, string suffix, EventHandler changed)
        {
            var s = new Stepper();
            s.Min = min; s.Max = max; s.Step = step; s.Suffix = suffix;
            s.ValueChanged += changed;
            return Place(s, x, y, w, S(34));
        }

        protected void ShowStrict(bool locked)
        {
            if (StrictBanner != null) StrictBanner.Visible = locked;
        }

        protected void Save() { Ctx.SettingsChanged(); }

        public virtual void RefreshView() { }
        public virtual void OnTick() { }
    }

    // ---------------------------------------------------------------- Websites
    class WebsitesPage : Page
    {
        readonly Toggle tglEnabled;
        readonly Segmented mode;
        readonly Label modeHint, listTitle, recentHint;
        readonly TextInput input;
        readonly FlatButton btnAdd, btnRemove, btnAllow, btnClear;
        readonly DarkList list, recent;
        readonly FlowLayoutPanel presets;
        bool? presetsForAllow;

        public WebsitesPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Websites", "Choose what your browser can open while you focus. Breaks are always unrestricted.");
            int full = Width - 2 * Pad;
            tglEnabled = AddToggle("Filter websites during focus", "Works in Chrome, Edge, Brave, Opera and Firefox (with “use system proxy settings”).", Pad, S(98), full, (s, e) =>
            {
                Ctx.Settings.BlockWebsites = tglEnabled.Checked;
                Save();
            });

            mode = Place(new Segmented("Allow only my list", "Block my list"), Pad, S(158), S(330), S(36));
            mode.SelectionChanged += (s, e) =>
            {
                Ctx.Settings.AllowListMode = mode.SelectedIndex == 0;
                Save();
            };
            modeHint = AddLabel("", Theme.F(9), Theme.Muted, Pad + S(346), S(158), full - S(346), S(36), ContentAlignment.MiddleLeft);

            int colW = S(330);
            listTitle = Section("", Pad, S(210), colW);
            input = Place(new TextInput("e.g. moodle.tum.de"), Pad, S(234), colW - S(84), TextInput.PreferredHeightNow);
            input.Submitted += (s, e) => AddSite();
            btnAdd = Button("Add", Glyph.Add, Pad + colW - S(76), S(234), S(76), S(36), (s, e) => AddSite());
            list = Place(new DarkList(), Pad, S(280), colW, S(250));
            list.EmptyText = "No websites yet";
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            btnRemove = Button("Remove", Glyph.Delete, Pad, S(540), S(120), S(34), (s, e) => RemoveSelected());

            int rx = Pad + colW + S(24), rw = full - colW - S(24);
            Section("Quick add", rx, S(210), rw);
            presets = new FlowLayoutPanel();
            presets.BackColor = Theme.Bg;
            presets.WrapContents = true;
            Place(presets, rx, S(234), rw, S(118));

            Section("Recently blocked", rx, S(358), rw);
            recent = Place(new DarkList(), rx, S(382), rw, S(148));
            recent.EmptyText = "Nothing blocked yet";
            recent.RightText = o => { var be = o as BlockEntry; return be == null ? null : be.Count + "×"; };
            btnAllow = Button("Allow", Glyph.Check, rx, S(540), S(110), S(34), (s, e) => AllowSelected());
            btnClear = Button("Clear", null, rx + S(118), S(540), S(90), S(34), (s, e) => { Ctx.ClearBlockLog(); RefreshRecent(); });

            recentHint = AddLabel("Blocked secure (https) pages show a browser connection error — that's Fokkus doing its job. " +
                                  "If a site you need breaks, allow its domain from “Recently blocked”.",
                Theme.F(8.5f), Theme.Muted, Pad, S(588), full, S(40));
        }

        List<string> CurrentList { get { return Ctx.Settings.AllowListMode ? Ctx.Settings.AllowedSites : Ctx.Settings.BlockedSites; } }

        void AddSite()
        {
            string d = HostRules.Normalize(input.Box.Text);
            if (d == null) { input.Flash(); return; }
            if (!CurrentList.Contains(d, StringComparer.OrdinalIgnoreCase)) CurrentList.Add(d);
            input.Box.Text = "";
            Save();
        }

        void RemoveSelected()
        {
            if (Ctx.StrictLocked) return;
            var sel = list.SelectedItem as string;
            if (sel == null) return;
            int idx = list.SelectedIndex;
            CurrentList.RemoveAll(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            Save();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
        }

        void AllowSelected()
        {
            var be = recent.SelectedItem as BlockEntry;
            if (be == null) return;
            if (Ctx.Settings.AllowListMode)
            {
                if (!Ctx.Settings.AllowedSites.Contains(be.Domain, StringComparer.OrdinalIgnoreCase)) Ctx.Settings.AllowedSites.Add(be.Domain);
            }
            else Ctx.Settings.BlockedSites.RemoveAll(x => be.Domain == x || be.Domain.EndsWith("." + x) || x.EndsWith("." + be.Domain));
            Ctx.RemoveFromBlockLog(be.Domain);
            Save();
        }

        void BuildPresets(bool allow)
        {
            presetsForAllow = allow;
            presets.SuspendLayout();
            foreach (Control c in presets.Controls) c.Dispose();
            presets.Controls.Clear();
            var tip = new ToolTip();
            foreach (var p in allow ? Presets.AllowSites : Presets.BlockSites)
            {
                var preset = p;
                var b = new FlatButton("+ " + p.Name);
                b.Font = Theme.F(9);
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + S(22), S(30));
                b.Margin = new Padding(0, 0, S(8), S(8));
                b.Click += (s, e) =>
                {
                    foreach (var item in preset.Items)
                        if (!CurrentList.Contains(item, StringComparer.OrdinalIgnoreCase)) CurrentList.Add(item);
                    Save();
                };
                tip.SetToolTip(b, string.Join(", ", p.Items));
                presets.Controls.Add(b);
            }
            presets.ResumeLayout();
        }

        public void RefreshRecent()
        {
            recent.SetItems(Ctx.BlockLog.Values.OrderByDescending(b => b.Last).Cast<object>());
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            bool locked = Ctx.StrictLocked;
            ShowStrict(locked);
            tglEnabled.SetSilently(s.BlockWebsites);
            mode.SetSilently(s.AllowListMode ? 0 : 1);
            modeHint.Text = s.AllowListMode
                ? "Strict: everything except these sites is blocked (subdomains included)."
                : "Relaxed: only these sites (and their subdomains) are blocked.";
            listTitle.Text = (s.AllowListMode ? "Allowed sites" : "Blocked sites").ToUpperInvariant() + "  (" + CurrentList.Count + ")";
            list.SetItems(CurrentList.Cast<object>());
            if (presetsForAllow != s.AllowListMode) BuildPresets(s.AllowListMode);
            btnAllow.Text = s.AllowListMode ? "Allow" : "Unblock";
            RefreshRecent();

            tglEnabled.Enabled = !locked;
            mode.Enabled = !locked;
            btnRemove.Enabled = !locked;
            btnAllow.Enabled = !locked;
            bool canAdd = !(locked && s.AllowListMode);
            input.Enabled = canAdd;
            btnAdd.Enabled = canAdd;
            presets.Enabled = canAdd;
        }
    }

    // ---------------------------------------------------------------- Apps
    class AppsPage : Page
    {
        readonly Toggle tglEnabled;
        readonly TextInput input;
        readonly ComboBox running;
        readonly FlatButton btnAdd, btnAddRunning, btnRemove;
        readonly DarkList list;
        readonly FlowLayoutPanel presets;
        readonly Label listTitle;

        public AppsPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Apps", "Close distracting Windows programs while you focus.");
            int full = Width - 2 * Pad, colW = S(330);
            tglEnabled = AddToggle("Close these apps during focus", "They get a polite close request first, then are ended if they keep running (e.g. Discord hiding in the tray).", Pad, S(98), full, (s, e) =>
            {
                Ctx.Settings.BlockApps = tglEnabled.Checked;
                Save();
            });

            listTitle = Section("", Pad, S(166), colW);
            input = Place(new TextInput("Process name, e.g. Discord"), Pad, S(190), colW - S(84), TextInput.PreferredHeightNow);
            input.Submitted += (s, e) => AddApp(input.Box.Text);
            btnAdd = Button("Add", Glyph.Add, Pad + colW - S(76), S(190), S(76), S(36), (s, e) => AddApp(input.Box.Text));

            running = new ComboBox();
            running.DropDownStyle = ComboBoxStyle.DropDownList;
            running.FlatStyle = FlatStyle.Standard;
            running.HandleCreated += (s, e) => Native.DarkControl(running.Handle);
            running.BackColor = Theme.Surface2;
            running.ForeColor = Theme.Text;
            running.Font = Theme.F(10);
            running.DrawMode = DrawMode.OwnerDrawFixed;
            running.ItemHeight = S(26);
            running.DrawItem += DrawComboItem;
            running.DropDown += (s, e) => LoadRunning();
            Place(running, Pad, S(236), colW - S(84), S(36));
            btnAddRunning = Button("Add", Glyph.Add, Pad + colW - S(76), S(236), S(76), S(34), (s, e) =>
            {
                var name = running.SelectedItem as string;
                if (name != null && !name.StartsWith("(")) AddApp(name);
            });
            LoadRunning();

            list = Place(new DarkList(), Pad, S(284), colW, S(246));
            list.EmptyText = "No apps yet";
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            btnRemove = Button("Remove", Glyph.Delete, Pad, S(540), S(120), S(34), (s, e) => RemoveSelected());

            int rx = Pad + colW + S(24), rw = full - colW - S(24);
            Section("Quick add", rx, S(166), rw);
            presets = new FlowLayoutPanel();
            presets.BackColor = Theme.Bg;
            Place(presets, rx, S(190), rw, S(90));
            var tip = new ToolTip();
            foreach (var p in Presets.Apps)
            {
                var preset = p;
                var b = new FlatButton("+ " + p.Name);
                b.Font = Theme.F(9);
                b.Size = new Size(TextRenderer.MeasureText(b.Text, b.Font).Width + S(22), S(30));
                b.Margin = new Padding(0, 0, S(8), S(8));
                b.Click += (s, e) =>
                {
                    foreach (var item in preset.Items)
                        if (!Ctx.Settings.BlockedApps.Contains(item, StringComparer.OrdinalIgnoreCase)) Ctx.Settings.BlockedApps.Add(item);
                    Save();
                };
                tip.SetToolTip(b, string.Join(", ", p.Items));
                presets.Controls.Add(b);
            }

            AddLabel("How to find a name: pick it from “Running apps” on the left, or open Task Manager → Details " +
                     "and use the name without “.exe”.\n\nBrowsers are not closed here — use the Websites page to filter what they can open.",
                Theme.F(9), Theme.Muted, rx, S(290), rw, S(140));
        }

        void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var b = new SolidBrush(sel ? Theme.Hover : Theme.Surface2)) e.Graphics.FillRectangle(b, e.Bounds);
            if (e.Index >= 0)
                Theme.DrawText(e.Graphics, running.Items[e.Index].ToString(), running.Font, Theme.Text,
                    new Rectangle(e.Bounds.X + S(8), e.Bounds.Y, e.Bounds.Width - S(8), e.Bounds.Height), Theme.Left);
        }

        void LoadRunning()
        {
            var names = AppBlocker.RunningWindowApps();
            running.BeginUpdate();
            running.Items.Clear();
            running.Items.Add("(Running apps)");
            foreach (var n in names) running.Items.Add(n);
            running.SelectedIndex = 0;
            running.EndUpdate();
        }

        void AddApp(string raw)
        {
            string n = AppBlocker.CleanName(raw);
            if (n.Length == 0) { input.Flash(); return; }
            if (string.Equals(n, "Fokkus", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "explorer", StringComparison.OrdinalIgnoreCase)) { input.Flash(); return; }
            if (!Ctx.Settings.BlockedApps.Contains(n, StringComparer.OrdinalIgnoreCase)) Ctx.Settings.BlockedApps.Add(n);
            input.Box.Text = "";
            Save();
        }

        void RemoveSelected()
        {
            if (Ctx.StrictLocked) return;
            var sel = list.SelectedItem as string;
            if (sel == null) return;
            Ctx.Settings.BlockedApps.RemoveAll(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            bool locked = Ctx.StrictLocked;
            ShowStrict(locked);
            tglEnabled.SetSilently(s.BlockApps);
            listTitle.Text = "CLOSED DURING FOCUS  (" + s.BlockedApps.Count + ")";
            list.SetItems(s.BlockedApps.Cast<object>());
            tglEnabled.Enabled = !locked;
            btnRemove.Enabled = !locked;
        }
    }

    // ---------------------------------------------------------------- Tips
    class TipsPage : Page
    {
        readonly Toggle tglTips, tglBreak;
        readonly Stepper interval;
        readonly Dictionary<string, Toggle> categories = new Dictionary<string, Toggle>();

        public TipsPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Wellbeing tips", "Small nudges to drink water, stretch, rest your eyes and breathe.");
            int full = Width - 2 * Pad;
            tglTips = AddToggle("Reminders during focus", "A small pop-up appears every few minutes while you focus. It never steals your keyboard focus.", Pad, S(98), full, (s, e) =>
            {
                Ctx.Settings.TipsEnabled = tglTips.Checked;
                Save();
            });
            AddLabel("Remind me every", Theme.F(10), Theme.Text, Pad + S(52), S(158), S(150), S(34), ContentAlignment.MiddleLeft);
            interval = AddStepper(Pad + S(202), S(158), S(150), 5, 90, 5, " min", (s, e) =>
            {
                Ctx.Settings.TipIntervalMinutes = interval.Value;
                Save();
            });
            tglBreak = AddToggle("Break suggestions", "When a break starts, get an idea of what to do: stretch, walk, refill your water, rest your eyes…", Pad, S(212), full, (s, e) =>
            {
                Ctx.Settings.BreakSuggestions = tglBreak.Checked;
                Save();
            });

            Section("Categories", Pad, S(280), full);
            int colW = full / 2;
            for (int i = 0; i < Tips.Categories.Length; i++)
            {
                string cat = Tips.Categories[i];
                int x = Pad + (i % 2) * colW, y = S(308) + (i / 2) * S(40);
                Toggle t = null;
                t = AddToggle(Tips.Icon(cat) + "  " + cat, null, x, y, colW - S(16), (s, e) =>
                {
                    var list = Ctx.Settings.DisabledTipCategories;
                    list.RemoveAll(c => c == cat);
                    if (!t.Checked) list.Add(cat);
                    Save();
                });
                t.Font = Theme.F(10);
                t.OnColor = Tips.CategoryColor(cat);
                categories[cat] = t;
            }

            Button("Show me a tip now", Glyph.Bulb, Pad, S(486), S(200), S(40), (s, e) => Ctx.ShowTipNow());
            AddLabel("Tip: the 20-20-20 rule (every 20 min, look 6 m away for 20 s) is one of the easiest ways to prevent tired eyes.",
                Theme.F(9), Theme.Muted, Pad, S(544), full, S(40));
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            tglTips.SetSilently(s.TipsEnabled);
            tglBreak.SetSilently(s.BreakSuggestions);
            interval.SetSilently(s.TipIntervalMinutes);
            interval.Enabled = s.TipsEnabled;
            foreach (var kv in categories) kv.Value.SetSilently(!s.DisabledTipCategories.Contains(kv.Key));
        }
    }

    // ---------------------------------------------------------------- Settings
    class SettingsPage : Page
    {
        readonly Stepper focus, shortBreak, longBreak, sessions, opacity;
        readonly Toggle autoBreaks, autoFocus, strict, sound, autostart, closeToTray;
        readonly Segmented miniMode;

        public SettingsPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Settings", "Tune the timer and how Fokkus behaves.");
            int lx = Pad, lw = S(330);
            int rx = Pad + lw + S(28), rw = Width - Pad - rx;

            Section("Durations", lx, S(98), lw);
            focus = Row("Focus", lx, S(124), 5, 180, 5, " min", v => Ctx.Settings.FocusMinutes = v);
            shortBreak = Row("Short break", lx, S(166), 1, 30, 1, " min", v => Ctx.Settings.ShortBreakMinutes = v);
            longBreak = Row("Long break", lx, S(208), 5, 60, 5, " min", v => Ctx.Settings.LongBreakMinutes = v);
            sessions = Row("Long break after", lx, S(250), 2, 8, 1, " sessions", v => Ctx.Settings.SessionsBeforeLongBreak = v);

            Section("Flow", lx, S(302), lw);
            autoBreaks = AddToggle("Start breaks automatically", null, lx, S(328), lw, (s, e) => { Ctx.Settings.AutoStartBreaks = autoBreaks.Checked; Save(); });
            autoFocus = AddToggle("Start focus automatically after breaks", null, lx, S(366), lw, (s, e) => { Ctx.Settings.AutoStartFocus = autoFocus.Checked; Save(); });
            strict = AddToggle("Strict mode", "No pause, skip, stop or quitting during focus, and the block lists can't be loosened.", lx, S(404), lw, (s, e) => { Ctx.Settings.StrictMode = strict.Checked; Save(); });

            Section("Mini timer", rx, S(98), rw);
            miniMode = Place(new Segmented("Always", "In sessions", "Off"), rx, S(124), rw, S(36));
            miniMode.SelectionChanged += (s, e) => { Ctx.Settings.MiniTimerMode = miniMode.SelectedIndex; Save(); };
            AddLabel("Opacity", Theme.F(10), Theme.Text, rx, S(170), S(150), S(34), ContentAlignment.MiddleLeft);
            opacity = AddStepper(rx + rw - S(150), S(170), S(150), 40, 100, 5, " %", (s, e) => { Ctx.Settings.MiniOpacity = opacity.Value; Save(); });

            Section("General", rx, S(224), rw);
            sound = AddToggle("Play a chime when a phase ends", null, rx, S(250), rw, (s, e) => { Ctx.Settings.SoundEnabled = sound.Checked; Save(); });
            autostart = AddToggle("Start with Windows (in the tray)", null, rx, S(288), rw, (s, e) => { Ctx.Settings.StartWithWindows = autostart.Checked; Save(); });
            closeToTray = AddToggle("Closing the window keeps it in the tray", null, rx, S(326), rw, (s, e) => { Ctx.Settings.CloseToTray = closeToTray.Checked; Save(); });

            Section("Safety", rx, S(378), rw);
            Button("Restore internet settings", Glyph.Refresh, rx, S(404), S(250), S(38), (s, e) => Ctx.RestoreInternet());
            AddLabel("If websites ever stay blocked after Fokkus closed unexpectedly, click this (or run the app once — it repairs itself on start).",
                Theme.F(8.5f), Theme.Muted, rx, S(448), rw, S(52));
            Button("Open data folder", Glyph.Folder, rx, S(510), S(250), S(38), (s, e) =>
            {
                try { Process.Start("explorer.exe", "\"" + AppSettings.Dir + "\""); } catch { }
            });
        }

        Stepper Row(string label, int x, int y, int min, int max, int step, string suffix, Action<int> set)
        {
            AddLabel(label, Theme.F(10), Theme.Text, x, y, S(170), S(34), ContentAlignment.MiddleLeft);
            Stepper st = null;
            st = AddStepper(x + S(330) - S(160), y, S(160), min, max, step, suffix, (s, e) => { set(st.Value); Save(); });
            return st;
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            bool locked = Ctx.StrictLocked;
            ShowStrict(locked);
            focus.SetSilently(s.FocusMinutes);
            shortBreak.SetSilently(s.ShortBreakMinutes);
            longBreak.SetSilently(s.LongBreakMinutes);
            sessions.SetSilently(s.SessionsBeforeLongBreak);
            autoBreaks.SetSilently(s.AutoStartBreaks);
            autoFocus.SetSilently(s.AutoStartFocus);
            strict.SetSilently(s.StrictMode);
            strict.Enabled = !locked;
            miniMode.SetSilently(s.MiniTimerMode);
            opacity.SetSilently(s.MiniOpacity);
            sound.SetSilently(s.SoundEnabled);
            autostart.SetSilently(s.StartWithWindows);
            closeToTray.SetSilently(s.CloseToTray);
        }
    }
}
