using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    // Category buttons: a preset is ON when all of its entries are in the list and switched on.
    static class Categories
    {
        public static bool IsOn(Preset p, List<string> list, List<string> off)
        {
            return p.Items.All(i => AppSettings.Has(list, i) && !AppSettings.Has(off, i));
        }

        // ON -> switch its entries off (except ones another active category needs). OFF -> add/switch them on.
        public static void Toggle(Preset p, IEnumerable<Preset> all, List<string> list, List<string> off)
        {
            if (IsOn(p, list, off))
            {
                var keep = new HashSet<string>(all.Where(o => o != p && IsOn(o, list, off)).SelectMany(o => o.Items), StringComparer.OrdinalIgnoreCase);
                foreach (var i in p.Items)
                    if (!keep.Contains(i) && !AppSettings.Has(off, i)) off.Add(i);
            }
            else
                foreach (var i in p.Items)
                {
                    if (!AppSettings.Has(list, i)) list.Add(i);
                    off.RemoveAll(x => string.Equals(x, i, StringComparison.OrdinalIgnoreCase));
                }
        }

        public static FlatButton Chip(string name, string tooltip, ToolTip tip, int pad)
        {
            var b = new FlatButton(name);
            b.Font = Theme.F(9);
            b.Size = new Size(TextRenderer.MeasureText("✓  " + name, b.Font).Width + pad, Theme.S(30));
            b.Margin = new Padding(0, 0, Theme.S(8), Theme.S(8));
            tip.SetToolTip(b, tooltip);
            b.Tag = name;
            return b;
        }

        public static void Style(FlatButton b, bool on)
        {
            string name = (string)b.Tag;
            b.Text = (on ? "✓  " : "+  ") + name;
            if (on)
            {
                b.BaseColor = Theme.Blend(Theme.Focus, Theme.Bg, 0.55f);
                b.HoverColor = Theme.Blend(Theme.Focus, Theme.Bg, 0.4f);
                b.TextColor = Theme.Text;
            }
            else b.SetPlain();
            b.Invalidate();
        }
    }

    // ---------------------------------------------------------------- Websites
    class WebsitesPage : Page
    {
        readonly Toggle tglEnabled;
        readonly Segmented mode;
        readonly Label modeHint, listTitle;
        readonly TextInput input;
        readonly FlatButton btnAdd, btnRemove, btnAllow;
        readonly SwitchList list;
        readonly DarkList recent;
        readonly FlowLayoutPanel presets;
        readonly List<FlatButton> chips = new List<FlatButton>();
        bool? presetsForAllow;

        public WebsitesPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Websites", "Choose what your browser can open while you focus. Breaks are always unrestricted.");
            int full = Width - 2 * Pad;
            tglEnabled = AddToggle("Filter websites during focus", "Works in Chrome, Edge, Brave, Opera and Firefox (with “use system proxy settings”).", Pad, S(98), full, (s, e) =>
            {
                if (!tglEnabled.Checked && Ctx.StrictLocked) { tglEnabled.SetSilently(true); return; }
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
            list = Place(new SwitchList(), Pad, S(280), colW, S(250));
            list.EmptyText = "No websites yet";
            list.IsOn = o => !AppSettings.Has(Ctx.Settings.CurrentDisabledSites, (string)o);
            list.Toggled += o => ToggleSite((string)o);
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            btnRemove = Button("Remove", Glyph.Delete, Pad, S(540), S(120), S(34), (s, e) => RemoveSelected());
            AddLabel("Use the switch to turn a site on or off without deleting it.", Theme.F(8.5f), Theme.Muted, Pad + S(128), S(540), colW - S(128), S(34), ContentAlignment.MiddleLeft);

            int rx = Pad + colW + S(24), rw = full - colW - S(24);
            Section("Categories · click to turn on/off", rx, S(210), rw);
            presets = new FlowLayoutPanel();
            presets.BackColor = Theme.Bg;
            presets.WrapContents = true;
            Place(presets, rx, S(234), rw, S(158));

            Section("Recently blocked", rx, S(398), rw);
            recent = Place(new DarkList(), rx, S(422), rw, S(108));
            recent.EmptyText = "Nothing blocked yet";
            recent.RightText = o => { var be = o as BlockEntry; return be == null ? null : be.Count + "×"; };
            btnAllow = Button("Allow", Glyph.Check, rx, S(540), S(110), S(34), (s, e) => AllowSelected());
            Button("Clear", null, rx + S(118), S(540), S(90), S(34), (s, e) => { Ctx.ClearBlockLog(); RefreshRecent(); });

            AddLabel("Blocked secure (https) pages show a browser connection error — that's Fokkus doing its job. " +
                     "If a site you need breaks, allow its domain from “Recently blocked”.",
                Theme.F(8.5f), Theme.Muted, Pad, S(588), full, S(40));
        }

        List<string> CurrentList { get { return Ctx.Settings.CurrentSites; } }
        List<string> CurrentOff { get { return Ctx.Settings.CurrentDisabledSites; } }

        // Allow list: switching something ON loosens it; block list: switching OFF loosens it.
        bool Loosens(bool turningOn) { return Ctx.Settings.AllowListMode ? turningOn : !turningOn; }

        bool StrictRefuses(bool turningOn)
        {
            if (Ctx.StrictLocked && Loosens(turningOn))
            {
                Ctx.Notify("\U0001F512", "Strict mode is on", "You can only make the list stricter until this focus session ends.", Theme.Focus, 5, null, null);
                return true;
            }
            return false;
        }

        void AddSite()
        {
            string d = HostRules.Normalize(input.Box.Text);
            if (d == null || StrictRefuses(true)) { input.Flash(); return; }
            if (!AppSettings.Has(CurrentList, d)) CurrentList.Add(d);
            CurrentOff.RemoveAll(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase));
            input.Box.Text = "";
            Save();
        }

        void ToggleSite(string site)
        {
            bool turningOn = AppSettings.Has(CurrentOff, site);
            if (StrictRefuses(turningOn)) return;
            if (turningOn) CurrentOff.RemoveAll(x => string.Equals(x, site, StringComparison.OrdinalIgnoreCase));
            else CurrentOff.Add(site);
            Save();
        }

        void RemoveSelected()
        {
            var sel = list.SelectedItem as string;
            if (sel == null || StrictRefuses(!Ctx.Settings.AllowListMode)) return;
            int idx = list.SelectedIndex;
            CurrentList.RemoveAll(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            CurrentOff.RemoveAll(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            Save();
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(idx, list.Items.Count - 1);
        }

        void AllowSelected()
        {
            var be = recent.SelectedItem as BlockEntry;
            if (be == null || StrictRefuses(Ctx.Settings.AllowListMode)) return;
            var s = Ctx.Settings;
            if (s.AllowListMode)
            {
                if (!AppSettings.Has(s.AllowedSites, be.Domain)) s.AllowedSites.Add(be.Domain);
                s.DisabledAllowedSites.RemoveAll(x => string.Equals(x, be.Domain, StringComparison.OrdinalIgnoreCase));
            }
            else
                foreach (var x in s.BlockedSites.Where(x => be.Domain == x || be.Domain.EndsWith("." + x) || x.EndsWith("." + be.Domain)).ToList())
                    if (!AppSettings.Has(s.DisabledBlockedSites, x)) s.DisabledBlockedSites.Add(x);
            Ctx.RemoveFromBlockLog(be.Domain);
            Save();
        }

        Preset[] CurrentPresets { get { return Ctx.Settings.AllowListMode ? Presets.AllowSites : Presets.BlockSites; } }

        void BuildPresets(bool allow)
        {
            presetsForAllow = allow;
            presets.SuspendLayout();
            foreach (Control c in presets.Controls) c.Dispose();
            presets.Controls.Clear();
            chips.Clear();
            var tip = new ToolTip();
            foreach (var p in allow ? Presets.AllowSites : Presets.BlockSites)
            {
                var preset = p;
                var b = Categories.Chip(p.Name, string.Join(", ", p.Items), tip, S(22));
                b.Click += (s, e) =>
                {
                    bool turningOn = !Categories.IsOn(preset, CurrentList, CurrentOff);
                    if (StrictRefuses(turningOn)) return;
                    Categories.Toggle(preset, CurrentPresets, CurrentList, CurrentOff);
                    Save();
                };
                chips.Add(b);
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
                ? "Strict: everything except the sites switched on is blocked (subdomains included)."
                : "Relaxed: only the sites switched on (and their subdomains) are blocked.";
            int on = s.ActiveSites(s.AllowListMode).Count();
            listTitle.Text = (s.AllowListMode ? "Allowed sites" : "Blocked sites").ToUpperInvariant() + "  (" + on + " of " + CurrentList.Count + " on)";
            list.SetItems(CurrentList.Cast<object>());
            if (presetsForAllow != s.AllowListMode) BuildPresets(s.AllowListMode);
            var ps = CurrentPresets;
            for (int i = 0; i < chips.Count && i < ps.Length; i++) Categories.Style(chips[i], Categories.IsOn(ps[i], CurrentList, CurrentOff));
            btnAllow.Text = s.AllowListMode ? "Allow" : "Unblock";
            RefreshRecent();
            mode.Enabled = !locked;
        }
    }

    // ---------------------------------------------------------------- Apps
    class AppsPage : Page
    {
        readonly Toggle tglEnabled;
        readonly TextInput input;
        readonly ComboBox running;
        readonly SwitchList list;
        readonly List<FlatButton> chips = new List<FlatButton>();
        readonly Label listTitle;

        public AppsPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Apps", "Close distracting Windows programs while you focus.");
            int full = Width - 2 * Pad, colW = S(330);
            tglEnabled = AddToggle("Close these apps during focus", "They get a polite close request first, then are ended if they keep running (e.g. Discord hiding in the tray).", Pad, S(98), full, (s, e) =>
            {
                if (!tglEnabled.Checked && Ctx.StrictLocked) { tglEnabled.SetSilently(true); return; }
                Ctx.Settings.BlockApps = tglEnabled.Checked;
                Save();
            });

            listTitle = Section("", Pad, S(166), colW);
            input = Place(new TextInput("Process name, e.g. Discord"), Pad, S(190), colW - S(84), TextInput.PreferredHeightNow);
            input.Submitted += (s, e) => AddApp(input.Box.Text);
            Button("Add", Glyph.Add, Pad + colW - S(76), S(190), S(76), S(36), (s, e) => AddApp(input.Box.Text));

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
            Button("Add", Glyph.Add, Pad + colW - S(76), S(236), S(76), S(34), (s, e) =>
            {
                var name = running.SelectedItem as string;
                if (name != null && !name.StartsWith("(")) AddApp(name);
            });
            LoadRunning();

            list = Place(new SwitchList(), Pad, S(284), colW, S(246));
            list.EmptyText = "No apps yet";
            list.IsOn = o => !AppSettings.Has(Ctx.Settings.DisabledApps, (string)o);
            list.Toggled += o => ToggleApp((string)o);
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            Button("Remove", Glyph.Delete, Pad, S(540), S(120), S(34), (s, e) => RemoveSelected());
            AddLabel("Switch an app off to let it run during focus without deleting it.", Theme.F(8.5f), Theme.Muted, Pad + S(128), S(540), colW - S(128), S(34), ContentAlignment.MiddleLeft);

            int rx = Pad + colW + S(24), rw = full - colW - S(24);
            Section("Categories · click to turn on/off", rx, S(166), rw);
            var presets = new FlowLayoutPanel();
            presets.BackColor = Theme.Bg;
            Place(presets, rx, S(190), rw, S(90));
            var tip = new ToolTip();
            foreach (var p in Presets.Apps)
            {
                var preset = p;
                var b = Categories.Chip(p.Name, string.Join(", ", p.Items), tip, S(22));
                b.Click += (s, e) =>
                {
                    bool turningOn = !Categories.IsOn(preset, Ctx.Settings.BlockedApps, Ctx.Settings.DisabledApps);
                    if (StrictRefuses(turningOn)) return;
                    Categories.Toggle(preset, Presets.Apps, Ctx.Settings.BlockedApps, Ctx.Settings.DisabledApps);
                    Save();
                };
                chips.Add(b);
                presets.Controls.Add(b);
            }

            AddLabel("How to find a name: pick it from “Running apps” on the left, or open Task Manager → Details " +
                     "and use the name without “.exe”.\n\nBrowsers are not closed here — use the Websites page to filter what they can open.",
                Theme.F(9), Theme.Muted, rx, S(290), rw, S(140));
        }

        // Blocked-app list: switching an app OFF (or removing it) loosens the lock.
        bool StrictRefuses(bool turningOn)
        {
            if (Ctx.StrictLocked && !turningOn)
            {
                Ctx.Notify("\U0001F512", "Strict mode is on", "You can only block more apps until this focus session ends.", Theme.Focus, 5, null, null);
                return true;
            }
            return false;
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
            if (n.Length == 0 || string.Equals(n, "Fokkus", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "explorer", StringComparison.OrdinalIgnoreCase))
            {
                input.Flash();
                return;
            }
            if (!AppSettings.Has(Ctx.Settings.BlockedApps, n)) Ctx.Settings.BlockedApps.Add(n);
            Ctx.Settings.DisabledApps.RemoveAll(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
            input.Box.Text = "";
            Save();
        }

        void ToggleApp(string app)
        {
            bool turningOn = AppSettings.Has(Ctx.Settings.DisabledApps, app);
            if (StrictRefuses(turningOn)) return;
            if (turningOn) Ctx.Settings.DisabledApps.RemoveAll(x => string.Equals(x, app, StringComparison.OrdinalIgnoreCase));
            else Ctx.Settings.DisabledApps.Add(app);
            Save();
        }

        void RemoveSelected()
        {
            var sel = list.SelectedItem as string;
            if (sel == null || StrictRefuses(false)) return;
            Ctx.Settings.BlockedApps.RemoveAll(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            Ctx.Settings.DisabledApps.RemoveAll(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            Save();
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            ShowStrict(Ctx.StrictLocked);
            tglEnabled.SetSilently(s.BlockApps);
            listTitle.Text = "CLOSED DURING FOCUS  (" + s.ActiveApps.Count() + " of " + s.BlockedApps.Count + " on)";
            list.SetItems(s.BlockedApps.Cast<object>());
            for (int i = 0; i < chips.Count && i < Presets.Apps.Length; i++)
                Categories.Style(chips[i], Categories.IsOn(Presets.Apps[i], s.BlockedApps, s.DisabledApps));
        }
    }
}
