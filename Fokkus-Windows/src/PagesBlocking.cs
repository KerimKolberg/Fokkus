using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    // Websites and Apps share one layout: two independent functions (allowed list / blocked list), each with its
    // own on/off switch, plus an editor for both lists with quick-add categories.
    abstract class FilterPage : Page
    {
        protected readonly Filter AllowFilter, BlockFilter;
        protected readonly Toggle TglAllow, TglBlock;
        protected readonly Label Status, TabHint, ListTitle;
        protected readonly Segmented Tabs;
        protected readonly TextInput Input;
        protected readonly DarkList List;
        protected readonly int Full, ColW, RightX, RightW;
        readonly FlowLayoutPanel chipPanel;
        readonly List<FlatButton> chips = new List<FlatButton>();
        readonly ToolTip tip = new ToolTip();
        int chipsFor = -1;

        protected abstract List<string> ListFor(bool allowed);
        protected abstract Preset[] PresetsFor(bool allowed);
        protected abstract string Clean(string raw);           // null = not a valid entry
        protected abstract string StatusText(AppSettings s);
        protected abstract string InUseHint(bool allowed);
        protected abstract string Noun { get; }

        protected bool AllowedTab { get { return Tabs.SelectedIndex == 0; } }
        protected List<string> CurrentList { get { return ListFor(AllowedTab); } }

        protected FilterPage(TrayContext ctx, Size size, Filter allowFilter, Filter blockFilter, string title, string subtitle,
                             string allowDesc, string blockDesc, string placeholder, int listTop)
            : base(ctx, size)
        {
            AllowFilter = allowFilter;
            BlockFilter = blockFilter;
            Header(title, subtitle);
            Full = Width - 2 * Pad;
            ColW = S(330);
            RightX = Pad + ColW + S(24);
            RightW = Full - ColW - S(24);
            int half = (Full - S(24)) / 2;

            TglAllow = AddToggle(TrayContext.FilterName(allowFilter), allowDesc, Pad, S(98), half, (s, e) => Switch(TglAllow, AllowFilter));
            TglBlock = AddToggle(TrayContext.FilterName(blockFilter), blockDesc, Pad + half + S(24), S(98), half, (s, e) => Switch(TglBlock, BlockFilter));
            Status = AddLabel("", Theme.FB(9.5f), Theme.Focus, Pad, S(168), Full, S(22), ContentAlignment.MiddleLeft);

            Tabs = Place(new Segmented("Allowed list", "Blocked list"), Pad, S(200), ColW, S(36));
            Tabs.SelectionChanged += (s, e) => RefreshView();
            TabHint = AddLabel("", Theme.F(9), Theme.Muted, RightX, S(200), RightW, S(36), ContentAlignment.MiddleLeft);

            ListTitle = Section("", Pad, S(248), ColW);
            Input = Place(new TextInput(placeholder), Pad, S(272), ColW - S(84), TextInput.PreferredHeightNow);
            Input.Submitted += (s, e) => AddItem(Input.Box.Text);
            Button("Add", Glyph.Add, Pad + ColW - S(76), S(272), S(76), S(36), (s, e) => AddItem(Input.Box.Text));
            List = Place(new DarkList(), Pad, S(listTop), ColW, S(534) - S(listTop));
            List.EmptyText = "Nothing on this list yet";
            List.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); };
            Button("Remove", Glyph.Delete, Pad, S(544), S(120), S(34), (s, e) => RemoveSelected());

            Section("Quick add · click to add a whole category", RightX, S(248), RightW);
            chipPanel = new FlowLayoutPanel();
            chipPanel.BackColor = Theme.Bg;
            chipPanel.WrapContents = true;
            Place(chipPanel, RightX, S(272), RightW, S(140));
        }

        void Switch(Toggle t, Filter f)
        {
            if (!Ctx.SetFilter(f, t.Checked)) t.SetSilently(Ctx.Settings.IsOn(f));
        }

        // Adding to the allowed list or removing from the blocked list loosens the lock (refused in strict mode).
        protected bool Refuse(bool loosens) { return Ctx.RefuseLoosening(loosens); }

        protected void AddItem(string raw)
        {
            string n = Clean(raw);
            if (n == null) { Input.Flash(); return; }
            if (Refuse(AllowedTab)) return;
            if (!AppSettings.Has(CurrentList, n)) CurrentList.Add(n);
            Input.Box.Text = "";
            Save();
            for (int i = 0; i < List.Items.Count; i++)
                if (string.Equals((string)List.Items[i], n, StringComparison.OrdinalIgnoreCase)) { List.SelectedIndex = i; break; }
        }

        void RemoveSelected()
        {
            var sel = List.SelectedItem as string;
            if (sel == null || Refuse(!AllowedTab)) return;
            int idx = List.SelectedIndex;
            CurrentList.RemoveAll(x => string.Equals(x, sel, StringComparison.OrdinalIgnoreCase));
            Save();
            if (List.Items.Count > 0) List.SelectedIndex = Math.Min(idx, List.Items.Count - 1);
        }

        void AddPreset(Preset p)
        {
            var list = CurrentList;
            if (p.Items.All(i => AppSettings.Has(list, i)) || Refuse(AllowedTab)) return;
            foreach (var i in p.Items)
                if (!AppSettings.Has(list, i)) list.Add(i);
            Save();
        }

        void BuildChips()
        {
            chipsFor = Tabs.SelectedIndex;
            chipPanel.SuspendLayout();
            var old = chipPanel.Controls.Cast<Control>().ToArray();
            chipPanel.Controls.Clear();
            foreach (var c in old) c.Dispose();
            chips.Clear();
            foreach (var p in PresetsFor(AllowedTab))
            {
                var preset = p;
                var b = new FlatButton(p.Name);
                b.Font = Theme.F(9);
                b.Size = new Size(TextRenderer.MeasureText("✓  " + p.Name, b.Font).Width + S(20), S(28));
                b.Margin = new Padding(0, 0, S(6), S(6));
                b.Tag = preset;
                b.Click += (s, e) => AddPreset(preset);
                chips.Add(b);
                chipPanel.Controls.Add(b);
            }
            chipPanel.ResumeLayout();
        }

        void StyleChips()
        {
            var list = CurrentList;
            foreach (var b in chips)
            {
                var p = (Preset)b.Tag;
                bool all = p.Items.All(i => AppSettings.Has(list, i));
                b.Text = (all ? "✓  " : "+  ") + p.Name;
                if (all)
                {
                    b.BaseColor = Theme.Blend(Theme.Focus, Theme.Bg, 0.55f);
                    b.HoverColor = b.BaseColor;
                    b.TextColor = Theme.Text;
                    b.Cursor = Cursors.Default;
                    b.Invalidate();
                }
                else
                {
                    b.SetPlain();
                    b.Cursor = Cursors.Hand;
                }
                tip.SetToolTip(b, (all ? "Already on your list: " : "Adds: ") + string.Join(", ", p.Items));
            }
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            ShowStrict(Ctx.StrictLocked);
            bool allowOn = s.IsOn(AllowFilter), blockOn = s.IsOn(BlockFilter);
            TglAllow.SetSilently(allowOn);
            TglBlock.SetSilently(blockOn);
            Status.Text = StatusText(s);
            Status.ForeColor = allowOn || blockOn ? Theme.Focus : Theme.Muted;

            Tabs.SetLabel(0, "Allowed list  (" + ListFor(true).Count + ")");
            Tabs.SetLabel(1, "Blocked list  (" + ListFor(false).Count + ")");
            Filter tabFilter = AllowedTab ? AllowFilter : BlockFilter;
            bool inUse = s.IsOn(tabFilter);
            TabHint.Text = inUse ? InUseHint(AllowedTab) : "“" + TrayContext.FilterName(tabFilter) + "” is off, so this list isn't used right now.";
            TabHint.ForeColor = inUse ? Theme.Text : Theme.Muted;
            ListTitle.Text = ((AllowedTab ? "Allowed " : "Blocked ") + Noun + (inUse ? "" : "  ·  not in use")).ToUpperInvariant();
            List.SetItems(CurrentList.Cast<object>());
            if (chipsFor != Tabs.SelectedIndex) BuildChips();
            StyleChips();
        }
    }

    // ---------------------------------------------------------------- Websites
    class WebsitesPage : FilterPage
    {
        readonly DarkList recent;

        public WebsitesPage(TrayContext ctx, Size size)
            : base(ctx, size, Filter.AllowSites, Filter.BlockSites, "Websites",
                   "Two separate functions — switch on one, both or none. Breaks are always unrestricted.",
                   "Only the sites on your allowed list (and their subdomains) work during focus.",
                   "Sites on your blocked list never work during focus — this wins over allowed.",
                   "e.g. moodle.tum.de", 318)
        {
            Section("Recently blocked", RightX, S(420), RightW);
            recent = Place(new DarkList(), RightX, S(444), RightW, S(90));
            recent.EmptyText = "Nothing blocked yet";
            recent.RightText = o => { var be = o as BlockEntry; return be == null ? null : be.Count + "×"; };
            Button("Allow", Glyph.Check, RightX, S(544), S(110), S(34), (s, e) => AllowSelected());
            Button("Clear", null, RightX + S(118), S(544), S(90), S(34), (s, e) => { Ctx.ClearBlockLog(); RefreshRecent(); });

            AddLabel("Works in Chrome, Edge, Brave, Opera and Firefox (with “use system proxy settings”). Blocked secure (https) pages " +
                     "show a browser connection error — that's Fokkus doing its job. If a site you need breaks, select it under " +
                     "“Recently blocked” and press Allow.",
                Theme.F(8.5f), Theme.Muted, Pad, S(590), Full, S(48));
        }

        protected override string Noun { get { return "websites"; } }
        protected override List<string> ListFor(bool allowed) { return allowed ? Ctx.Settings.AllowedSites : Ctx.Settings.BlockedSites; }
        protected override Preset[] PresetsFor(bool allowed) { return allowed ? Presets.AllowSites : Presets.BlockSites; }
        protected override string Clean(string raw) { return HostRules.Normalize(raw); }

        protected override string InUseHint(bool allowed)
        {
            return allowed ? "In use — during focus only these sites work." : "In use — during focus these sites never work.";
        }

        protected override string StatusText(AppSettings s)
        {
            int a = s.AllowedSites.Count, b = s.BlockedSites.Count;
            if (s.AllowSitesOn && s.BlockSitesOn) return "During focus: only your " + a + " allowed sites work, and your " + b + " blocked sites never do.";
            if (s.AllowSitesOn) return "During focus: only your " + a + " allowed sites work. Everything else is blocked.";
            if (s.BlockSitesOn) return "During focus: everything works except your " + b + " blocked sites.";
            return "Both off — every website works during focus.";
        }

        // Makes a recently blocked site work: takes it off the blocked list and, if the allowed list is in use, adds it there.
        void AllowSelected()
        {
            var be = recent.SelectedItem as BlockEntry;
            if (be == null || Refuse(true)) return;
            var s = Ctx.Settings;
            string d = be.Domain;
            if (s.BlockSitesOn)
                s.BlockedSites.RemoveAll(x => x == d || d.EndsWith("." + x, StringComparison.Ordinal) || x.EndsWith("." + d, StringComparison.Ordinal));
            if (s.AllowSitesOn && !AppSettings.Has(s.AllowedSites, d)) s.AllowedSites.Add(d);
            Ctx.RemoveFromBlockLog(d);
            Save();
        }

        public void RefreshRecent()
        {
            recent.SetItems(Ctx.BlockLog.Values.OrderByDescending(b => b.Last).Cast<object>());
        }

        public override void RefreshView()
        {
            base.RefreshView();
            RefreshRecent();
        }
    }

    // ---------------------------------------------------------------- Apps
    class AppsPage : FilterPage
    {
        readonly ComboBox running;

        public AppsPage(TrayContext ctx, Size size)
            : base(ctx, size, Filter.AllowApps, Filter.BlockApps, "Apps",
                   "The same two functions for Windows programs — switch on one, both or none.",
                   "Apps that aren't on your allowed list are minimized during focus — never closed.",
                   "Apps on your blocked list are closed during focus — this wins over allowed.",
                   "Process name, e.g. Discord", 364)
        {
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
            Place(running, Pad, S(318), ColW - S(84), S(36));
            Button("Add", Glyph.Add, Pad + ColW - S(76), S(318), S(76), S(34), (s, e) =>
            {
                var name = running.SelectedItem as string;
                if (name != null && !name.StartsWith("(")) AddItem(name);
            });
            LoadRunning();

            AddLabel("How to find a name: pick it from “Running apps” on the left, or open Task Manager → Details and use the " +
                     "name without “.exe”.\n\nWindows itself, File Explorer and Fokkus are never minimized or closed. " +
                     "What your browser can open is set on the Websites page.",
                Theme.F(9), Theme.Muted, RightX, S(420), RightW, S(114));
            AddLabel("Minimized apps wait in the taskbar until the break, so nothing is lost. Blocked apps get a polite close " +
                     "request first and are ended if they keep running (e.g. Discord hiding in the tray).",
                Theme.F(8.5f), Theme.Muted, Pad, S(590), Full, S(48));
        }

        protected override string Noun { get { return "apps"; } }
        protected override List<string> ListFor(bool allowed) { return allowed ? Ctx.Settings.AllowedApps : Ctx.Settings.BlockedApps; }
        protected override Preset[] PresetsFor(bool allowed) { return allowed ? Presets.AllowApps : Presets.Apps; }

        protected override string Clean(string raw)
        {
            string n = AppBlocker.CleanName(raw);
            return n.Length == 0 || AppGuard.IsEssential(n) ? null : n;
        }

        protected override string InUseHint(bool allowed)
        {
            return allowed ? "In use — during focus any other app is minimized." : "In use — these apps are closed during focus.";
        }

        protected override string StatusText(AppSettings s)
        {
            int a = s.AllowedApps.Count, b = s.BlockedApps.Count;
            if (s.AllowAppsOn && s.BlockAppsOn) return "During focus: your " + b + " blocked apps are closed, and any app that isn't allowed is minimized.";
            if (s.AllowAppsOn) return "During focus: only your " + a + " allowed apps can stay open in front — others are minimized.";
            if (s.BlockAppsOn) return "During focus: your " + b + " blocked apps are closed. Everything else runs normally.";
            return "Both off — every app can be used during focus.";
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
    }
}
