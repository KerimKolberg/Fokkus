using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    // Button that shows the subject you're studying (colour dot + name + chevron).
    class SubjectButton : PaintedControl
    {
        public Subject Subject;
        bool hover;

        public SubjectButton() { Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ParentBack);
            Theme.FillRound(g, hover ? Theme.Hover : Theme.Surface2, new RectangleF(0, 0, Width - 1, Height - 1), Theme.S(10));
            using (var b = new SolidBrush(Subject != null ? Theme.SubjectColor(Subject) : Theme.SeriesOther))
                g.FillEllipse(b, Theme.S(14), Height / 2 - Theme.S(5), Theme.S(10), Theme.S(10));
            Theme.DrawText(g, Subject != null ? Subject.Name : "No subject", Theme.FB(10), Subject != null ? Theme.Text : Theme.Muted,
                new Rectangle(Theme.S(32), 0, Width - Theme.S(64), Height), Theme.Left);
            Theme.DrawText(g, Glyph.Down, Theme.Icons(8), Theme.Muted, new Rectangle(Width - Theme.S(30), 0, Theme.S(20), Height), Theme.Center);
        }
    }

    // ---------------------------------------------------------------- Timer
    class TimerPage : Page
    {
        readonly RingView ring;
        readonly FlatButton btnMain, btnSkip, btnStop;
        readonly Label lblState, lblInfo, planTitle;
        readonly MotivationCard motto;
        readonly SubjectButton subject;
        readonly TodayPlanView plan;
        readonly Filter[] quickFilters = { Filter.AllowSites, Filter.BlockSites, Filter.AllowApps, Filter.BlockApps };
        readonly QuickToggle[] quick = new QuickToggle[4];
        readonly FlatButton btnProfile, btnMusic;
        readonly QuickToggle music;
        string lastKey;

        static string QuickName(Filter f)
        {
            switch (f)
            {
                case Filter.AllowSites: return "Allowed sites";
                case Filter.BlockSites: return "Blocked sites";
                case Filter.AllowApps: return "Allowed apps";
                default: return "Blocked apps";
            }
        }

        public TimerPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Focus timer", "");
            Button("Mini timer", Glyph.Minimize, Width - Pad - S(150), S(28), S(150), S(36), (s, e) => Ctx.SwitchToMini());
            // Session profile: one click sets all four blocking functions (+ strict mode).
            btnProfile = Button("Profile", Glyph.Shield, Width - Pad - S(150) - S(10) - S(220), S(28), S(220), S(36), (s, e) => ShowProfileMenu());
            new ToolTip().SetToolTip(btnProfile, "Session profile: switches the blocking functions in one go");

            int leftW = S(420), cx = Pad + leftW / 2;
            int rs = S(300);
            ring = Place(new RingView(), cx - rs / 2, S(96), rs, rs);
            int by = S(96) + rs + S(16);
            int wMain = S(160), wSec = S(108), gap = S(10);
            int x = cx - (wMain + 2 * wSec + 2 * gap) / 2;
            btnMain = Button("Start", Glyph.Play, x, by, wMain, S(48), (s, e) => Ctx.ToggleTimer());
            btnMain.Font = Theme.FB(11);
            btnSkip = Button("Skip", Glyph.Next, x + wMain + gap, by, wSec, S(48), (s, e) => Ctx.Skip());
            btnStop = Button("Stop", Glyph.Stop, x + wMain + 2 * gap + wSec, by, wSec, S(48), (s, e) => Ctx.Stop());
            lblState = AddLabel("", Theme.FB(9.5f), Theme.Focus, Pad, by + S(60), leftW, S(42), ContentAlignment.TopCenter);
            lblInfo = AddLabel("", Theme.F(9), Theme.Muted, Pad, by + S(104), leftW, S(22), ContentAlignment.MiddleCenter);
            // Quick switches for the four functions: decide per session what gets blocked
            // (e.g. only "Blocked websites" for a programming session).
            int qw = (leftW - S(10)) / 2;
            string[] glyphs = { Glyph.Globe, Glyph.Shield, Glyph.Apps, Glyph.Close };
            for (int i = 0; i < quick.Length; i++)
            {
                var f = quickFilters[i];
                quick[i] = Place(new QuickToggle(QuickName(f), glyphs[i]), Pad + (i % 2) * (qw + S(10)), by + S(134) + (i / 2) * S(44), qw, S(38));
                quick[i].Click += (s, e) => Ctx.SetFilter(f, !Ctx.Settings.IsOn(f));
            }

            int rx = Pad + leftW + S(24), rw = Width - Pad - rx;
            motto = Place(new MotivationCard(), rx, S(96), rw, S(136));
            Section("Studying", rx, S(246), rw);
            subject = Place(new SubjectButton(), rx, S(270), rw, S(40));
            subject.Click += (s, e) => ShowSubjectMenu();
            planTitle = Section("Today's plan", rx, S(324), rw - S(70));
            var edit = Button("Edit", Glyph.Edit, rx + rw - S(66), S(318), S(66), S(28), (s, e) => Ctx.OpenPage(1));
            edit.Font = Theme.F(9);
            plan = Place(new TodayPlanView(), rx, S(350), rw, S(194));
            plan.SubjectClicked += subj => Ctx.SelectSubject(subj.Id);

            // Focus music: on/off + station menu. It only plays while a focus phase runs.
            music = Place(new QuickToggle("Music", Glyph.Music), rx, S(552), rw - S(46), S(40));
            music.Click += (s, e) => Ctx.SetMusic(!Ctx.Settings.MusicOn);
            btnMusic = Button("", Glyph.Down, rx + rw - S(40), S(552), S(40), S(40), (s, e) => ShowMusicMenu());
            var tip = new ToolTip();
            tip.SetToolTip(music, "Focus music plays while you focus and pauses on breaks");
            tip.SetToolTip(btnMusic, "Station and volume");
        }

        void ShowProfileMenu()
        {
            var m = new ContextMenuStrip();
            m.Renderer = new DarkMenuRenderer();
            m.Font = Theme.F(10);
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            Ctx.FillProfileMenu(m.Items);
            m.Closed += (a, b) => BeginInvoke(new Action(m.Dispose));
            m.Show(btnProfile, new Point(0, btnProfile.Height + S(2)));
        }

        void ShowMusicMenu()
        {
            var m = new ContextMenuStrip();
            m.Renderer = new DarkMenuRenderer();
            m.Font = Theme.F(10);
            m.ShowCheckMargin = true;
            m.ShowImageMargin = false;
            Ctx.FillMusicMenu(m.Items);
            m.Closed += (a, b) => BeginInvoke(new Action(m.Dispose));
            m.Show(btnMusic, new Point(btnMusic.Width - S(300), btnMusic.Height + S(2)));
        }

        void ShowSubjectMenu()
        {
            var m = new ContextMenuStrip();
            m.Renderer = new DarkMenuRenderer();
            m.Font = Theme.F(10);
            m.ShowCheckMargin = true;
            m.ShowImageMargin = true;
            var today = Planner.Today(Ctx.Settings, Ctx.Sessions).ToDictionary(r => r.Subject.Id);
            foreach (var s in Ctx.Settings.Subjects)
            {
                var subj = s;
                PlanRow row;
                string extra = today.TryGetValue(s.Id, out row) && row.Target > 0 ? "   " + row.Done + "/" + row.Target + " today" : "";
                var item = new ToolStripMenuItem(s.Name + extra, Dot(Theme.SubjectColor(s)), (a, b) => Ctx.SelectSubject(subj.Id));
                item.Checked = s.Id == Ctx.Settings.ActiveSubjectId;
                m.Items.Add(item);
            }
            var none = new ToolStripMenuItem("No subject", Dot(Theme.SeriesOther), (a, b) => Ctx.SelectSubject(null));
            none.Checked = Ctx.Settings.ActiveSubjectId == null;
            m.Items.Add(none);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Edit study plan…", null, (a, b) => Ctx.OpenPage(1)));
            m.Closed += (a, b) => BeginInvoke(new Action(m.Dispose));
            m.Show(subject, new Point(0, subject.Height + S(2)));
        }

        static Bitmap Dot(Color c)
        {
            int sz = Theme.S(16);
            var bmp = new Bitmap(sz, sz);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(c)) g.FillEllipse(b, sz * 0.2f, sz * 0.2f, sz * 0.6f, sz * 0.6f);
            }
            return bmp;
        }

        public override void RefreshView()
        {
            var s = Ctx.Settings;
            var eng = Ctx.Engine;
            Subtitle.Text = s.FocusMinutes + " min focus  ·  " + s.ShortBreakMinutes + " min break  ·  " +
                            s.LongBreakMinutes + " min long break after " + s.SessionsBeforeLongBreak + "   (Space = start/pause)";

            Color pc = eng.State == RunState.Idle ? Theme.Focus : Theme.PhaseColor(eng.Phase);
            switch (eng.State)
            {
                case RunState.Running: btnMain.Text = "Pause"; btnMain.Glyph = Glyph.Pause; break;
                case RunState.Paused: btnMain.Text = "Resume"; btnMain.Glyph = Glyph.Play; break;
                case RunState.Ready: btnMain.Text = "Start " + (eng.Phase == Phase.Focus ? "focus" : "break"); btnMain.Glyph = Glyph.Play; break;
                default: btnMain.Text = "Lock in"; btnMain.Glyph = Glyph.Play; break;
            }
            btnMain.SetAccent(pc);
            btnMain.Enabled = !(eng.State == RunState.Running && Ctx.StrictLocked);
            btnSkip.Enabled = eng.State != RunState.Idle && !Ctx.StrictLocked;
            btnStop.Enabled = eng.State != RunState.Idle && !Ctx.StrictLocked;

            if (eng.IsLockedIn)
            {
                var parts = new List<string>();
                if (s.AllowSitesOn) parts.Add("allowed sites only");
                else if (s.BlockSitesOn) parts.Add(s.BlockedSites.Count + " sites blocked");
                if (s.AllowAppsOn) parts.Add("allowed apps only");
                else if (s.BlockAppsOn && s.BlockedApps.Count > 0) parts.Add(s.BlockedApps.Count + " apps closed");
                lblState.Text = "\U0001F512  LOCKED IN" + (Ctx.StrictLocked ? " · STRICT" : "") + (parts.Count > 0 ? "\n" + string.Join(", ", parts) : "");
                lblState.ForeColor = Theme.Focus;
            }
            else if (eng.State == RunState.Idle)
            {
                lblState.Text = "Press “Lock in” to start a focus session";
                lblState.ForeColor = Theme.Muted;
            }
            else
            {
                lblState.Text = eng.Phase == Phase.Focus ? "Next up: focus. Blocking starts when you press Start." : "Break time — everything is unlocked.\nMove, drink, breathe.";
                lblState.ForeColor = Theme.PhaseColor(eng.Phase);
            }

            int pomos = Ctx.Sessions.PomodorosOn(DateTime.Today, null), minutes = Ctx.Sessions.MinutesOn(DateTime.Today);
            int done, target;
            Planner.TodayTotals(s, Ctx.Sessions, out done, out target);
            lblInfo.Text = "Today: " + pomos + (pomos == 1 ? " pomodoro" : " pomodoros") + "  ·  " + Theme.FormatMinutes(minutes) + " focused";
            planTitle.Text = "TODAY'S PLAN" + (target > 0 ? "  ·  " + done + " / " + target : "");
            subject.Subject = Planner.Active(s);
            subject.Invalidate();
            for (int i = 0; i < quick.Length; i++) quick[i].SetChecked(s.IsOn(quickFilters[i]));
            btnProfile.Text = "Profile: " + Ctx.ProfileLabel;
            var station = Stations.Find(s, s.MusicStation);
            music.Text = Ctx.Music.Error != null && s.MusicOn ? "Can't play" : station.Name;
            music.SetChecked(s.MusicOn);
            music.Invalidate();
            plan.SetRows(Planner.Today(s, Ctx.Sessions), s.ActiveSubjectId);
            lastKey = null;
            OnTick();
        }

        public override void OnTick()
        {
            var eng = Ctx.Engine;
            int done, target;
            Planner.TodayTotals(Ctx.Settings, Ctx.Sessions, out done, out target);
            string caption = eng.State == RunState.Idle ? "Fokkus" : target > 0 ? "Today's plan: " + done + " of " + target : Theme.PhaseName(eng.Phase);
            motto.Set(Motivation.Current(eng, done, target), caption);

            string time = Theme.FormatTime(eng.Remaining);
            string key = time + eng.State + eng.Phase + eng.CompletedInCycle;
            if (key == lastKey) return;
            lastKey = key;
            ring.TimeText = time;
            ring.RingColor = eng.State == RunState.Idle ? Theme.Focus : Theme.PhaseColor(eng.Phase);
            ring.Remaining = eng.State == RunState.Idle ? 1f : (float)(1 - eng.Progress);
            ring.Dim = eng.State == RunState.Paused || eng.State == RunState.Ready || eng.State == RunState.Idle;
            ring.PhaseText = Theme.PhaseName(eng.Phase).ToUpperInvariant();
            ring.Dots = eng.SessionsPerCycle;
            ring.DotsFilled = eng.CompletedInCycle;
            var subj = Planner.Active(Ctx.Settings);
            switch (eng.State)
            {
                case RunState.Idle: ring.SubText = subj != null ? subj.Name : "ready when you are"; break;
                case RunState.Paused: ring.SubText = "paused"; break;
                case RunState.Ready: ring.SubText = "press start"; break;
                default: ring.SubText = eng.Phase == Phase.Focus ? (subj != null ? subj.Name : "stay locked in") : "relax & recharge"; break;
            }
            ring.Invalidate();
        }
    }

    // ---------------------------------------------------------------- Plan
    class PlanPage : Page
    {
        const int MaxSubjects = 10;
        readonly PlanGrid grid;
        readonly TextInput input;
        readonly FlatButton btnRename, btnRemove, btnUp, btnDown, btnDeadline;
        readonly Label info;
        readonly Toggle autoNext;

        public PlanPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Study plan", "Pomodoros per subject for each weekday. Click a cell to add one, right-click (or scroll) to remove one.");
            int full = Width - 2 * Pad;
            input = Place(new TextInput("New subject, e.g. Linear Algebra"), Pad, S(96), S(236), TextInput.PreferredHeightNow);
            input.Submitted += (s, e) => AddSubject();
            Button("Add", Glyph.Add, Pad + S(244), S(96), S(76), S(36), (s, e) => AddSubject());
            btnRename = Button("Rename", Glyph.Edit, Pad + S(328), S(96), S(100), S(36), (s, e) => Rename());
            btnDeadline = Button("Deadline", Glyph.Calendar, Pad + S(436), S(96), S(112), S(36), (s, e) => EditDeadline());
            btnUp = Button("", Glyph.Up, Width - Pad - S(120), S(96), S(36), S(36), (s, e) => MoveSelected(-1));
            btnDown = Button("", Glyph.Down, Width - Pad - S(78), S(96), S(36), S(36), (s, e) => MoveSelected(1));
            btnRemove = Button("", Glyph.Delete, Width - Pad - S(36), S(96), S(36), S(36), (s, e) => Remove());
            var tips = new ToolTip();
            tips.SetToolTip(btnDeadline, "Exam or due date for the selected subject");
            tips.SetToolTip(btnUp, "Move up");
            tips.SetToolTip(btnDown, "Move down");
            tips.SetToolTip(btnRemove, "Remove the selected subject");

            grid = Place(new PlanGrid(), Pad, S(148), full, S(200));
            grid.Settings = Ctx.Settings;
            grid.Changed += (s, e) => { Ctx.Settings.Save(); UpdateInfo(); Ctx.PlanChanged(); };
            grid.SelectionChanged += (s, e) => UpdateButtons();

            info = AddLabel("", Theme.F(9), Theme.Muted, Pad, S(400), full, S(22), ContentAlignment.MiddleLeft);
            autoNext = AddToggle("Move on to the next subject when today's target for one is reached", null, Pad, S(430), full, (s, e) =>
            {
                Ctx.Settings.AutoNextSubject = autoNext.Checked;
                Save();
            });
        }

        Subject Selected { get { return Planner.Find(Ctx.Settings, grid.SelectedId); } }

        void AddSubject()
        {
            string name = (input.Box.Text ?? "").Trim();
            var list = Ctx.Settings.Subjects;
            if (name.Length == 0 || list.Count >= MaxSubjects || list.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                input.Flash();
                return;
            }
            var s = new Subject();
            s.Name = name.Length > 40 ? name.Substring(0, 40) : name;
            s.ColorIndex = Ctx.Settings.NextColorIndex();
            list.Add(s);
            if (Ctx.Settings.PlanSince == null) Ctx.Settings.PlanSince = DateTime.Today.ToString("yyyy-MM-dd");
            grid.SelectedId = s.Id;
            if (Ctx.Settings.ActiveSubjectId == null) Ctx.Settings.ActiveSubjectId = s.Id;
            input.Box.Text = "";
            Save();
        }

        void Rename()
        {
            var s = Selected;
            string name = (input.Box.Text ?? "").Trim();
            if (s == null || name.Length == 0) { input.Flash(); return; }
            s.Name = name.Length > 40 ? name.Substring(0, 40) : name;
            input.Box.Text = "";
            Save();
        }

        void EditDeadline()
        {
            var s = Selected;
            if (s == null) return;
            if (Dialogs.EditDeadline(FindForm(), s)) { Save(); Ctx.PlanChanged(); }
        }

        void MoveSelected(int dir)
        {
            var s = Selected;
            if (s == null) return;
            var list = Ctx.Settings.Subjects;
            int i = list.IndexOf(s), j = i + dir;
            if (j < 0 || j >= list.Count) return;
            list[i] = list[j];
            list[j] = s;
            Save();
        }

        void Remove()
        {
            var s = Selected;
            if (s == null) return;
            var answer = MessageBox.Show(FindForm(), "Remove “" + s.Name + "” from your plan?\n\nTime you already focused on it stays in your statistics (as “Other”).",
                "Fokkus", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            Ctx.Settings.Subjects.Remove(s);
            if (Ctx.Settings.ActiveSubjectId == s.Id) Ctx.Settings.ActiveSubjectId = null;
            grid.SelectedId = null;
            Save();
        }

        void UpdateButtons()
        {
            bool sel = Selected != null;
            btnRename.Enabled = sel;
            btnDeadline.Enabled = sel;
            btnRemove.Enabled = sel;
            btnUp.Enabled = sel;
            btnDown.Enabled = sel;
        }

        void UpdateInfo()
        {
            var s = Ctx.Settings;
            int week = s.Subjects.Sum(x => x.Targets.Sum());
            info.Text = week == 0
                ? "1 pomodoro = " + s.FocusMinutes + " min of focus. Add subjects and click the days to plan your week."
                : "1 pomodoro = " + s.FocusMinutes + " min of focus  ·  this week: " + week + " pomodoros ≈ " + Theme.FormatMinutes(week * s.FocusMinutes) + " of focus";
        }

        public override void RefreshView()
        {
            grid.Settings = Ctx.Settings;
            if (grid.SelectedId != null && Selected == null) grid.SelectedId = null;
            grid.Height = grid.PreferredGridHeight;
            grid.Invalidate();
            info.Top = grid.Bottom + S(8);
            autoNext.Top = info.Bottom + S(8);
            autoNext.SetSilently(Ctx.Settings.AutoNextSubject);
            UpdateInfo();
            UpdateButtons();
        }
    }

    // ---------------------------------------------------------------- Statistics
    class StatsPage : Page
    {
        StatRange range = StatRange.Week;
        DateTime anchor = DateTime.Today;
        readonly Segmented seg;
        readonly FlatButton btnNext;
        readonly Label title;
        readonly StatTile[] tiles = new StatTile[4];
        readonly LegendView legend;
        readonly BarChart chart;
        readonly SubjectBreakdown breakdown;
        readonly Segmented lower;
        readonly Label lowerHint;
        readonly DarkList list;

        public StatsPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Statistics", "Your focused time. Hover the bars for details; the white marks show your plan.");
            int full = Width - 2 * Pad;
            seg = Place(new Segmented("Day", "Week", "Month", "Year"), Pad, S(96), S(280), S(34));
            seg.SetSilently(1);
            seg.SelectionChanged += (s, e) => { range = (StatRange)seg.SelectedIndex; RefreshView(); };
            Button("", Glyph.Left, Pad + S(292), S(96), S(38), S(34), (s, e) => { anchor = Analytics.Shift(range, anchor, -1); RefreshView(); });
            btnNext = Button("", Glyph.Right, Pad + S(336), S(96), S(38), S(34), (s, e) => { anchor = Analytics.Shift(range, anchor, 1); RefreshView(); });
            title = AddLabel("", Theme.FB(10.5f), Theme.Text, Pad + S(386), S(96), full - S(386) - S(92), S(34), ContentAlignment.MiddleLeft);
            Button("Today", null, Width - Pad - S(84), S(96), S(84), S(34), (s, e) => { anchor = DateTime.Today; RefreshView(); });

            int tw = (full - 3 * S(12)) / 4;
            for (int i = 0; i < 4; i++) tiles[i] = Place(new StatTile(), Pad + i * (tw + S(12)), S(142), tw, S(76));
            legend = Place(new LegendView(), Pad, S(228), full, S(22));
            chart = Place(new BarChart(), Pad, S(252), full, S(228));

            // Below the chart: time per subject, your notes, or the distractions you tried to open.
            lower = Place(new Segmented("By subject", "Notes", "Distractions"), Pad, S(488), S(380), S(30));
            lower.SelectionChanged += (s, e) => RefreshView();
            lowerHint = AddLabel("", Theme.F(8.5f), Theme.Muted, Pad + S(392), S(488), full - S(392) - S(162), S(30), ContentAlignment.MiddleRight);
            var review = Button("Weekly review", Glyph.Note, Width - Pad - S(150), S(488), S(150), S(30), (s, e) => Ctx.ShowReview(anchor));
            review.Font = Theme.F(9);
            breakdown = Place(new SubjectBreakdown(), Pad, S(526), full, S(124));
            list = Place(new DarkList(), Pad, S(526), full, S(124));
            list.ItemHeight = S(26);
            list.Font = Theme.F(9.5f);
            list.DoubleClick += (s, e) => EditNote();
            list.Visible = false;
            new ToolTip().SetToolTip(list, "Double-click a note to edit it");
        }

        // A note in the list (double-click to edit).
        class NoteItem
        {
            public FocusRecord Record;
            public string Text;
            public override string ToString() { return Text; }
        }

        void EditNote()
        {
            var item = list.SelectedItem as NoteItem;
            if (item == null) return;
            string t = Dialogs.AskText(FindForm(), "Note", "What did you finish?", item.Record.Note);
            if (t == null) return;
            Ctx.Sessions.SetNote(item.Record, t);
            RefreshView();
        }

        internal void ShowRange(StatRange r)
        {
            range = r;
            anchor = DateTime.Today;
            seg.SetSilently((int)r);
            RefreshView();
        }

        internal void DebugHover(int bucket)
        {
            chart.DebugHover(bucket);
        }

        internal void ShowLower(int tab) { lower.SelectedIndex = tab; }

        public override void RefreshView()
        {
            var r = Analytics.Compute(Ctx.Settings, Ctx.Sessions, range, anchor);
            title.Text = r.Title;
            btnNext.Enabled = r.End <= DateTime.Today;
            string period = range == StatRange.Day ? "on this day" : range == StatRange.Week ? "this week" : range == StatRange.Month ? "this month" : "this year";
            tiles[0].Set("Focused", Theme.Compact(r.TotalMinutes), period);
            tiles[1].Set("Pomodoros", r.Pomodoros.ToString(), r.Planned > 0 ? "of " + r.Planned + " planned so far" : "completed");
            tiles[2].Set(r.DailyAverageLabel, Theme.Compact(r.DailyAverage), "per day");
            tiles[3].Set("Streak", r.Streak + (r.Streak == 1 ? " day" : " days"), "in a row with a pomodoro");
            var present = r.Series.Where((s, i) => r.Buckets.Any(b => b.Minutes[i] > 0.01)).ToList();
            legend.Set(present, r.Buckets.Any(b => b.Target > 0));
            chart.SetData(r);
            breakdown.SetData(r);

            int tab = lower.SelectedIndex;
            breakdown.Visible = tab == 0;
            list.Visible = tab != 0;
            if (tab == 1)
            {
                // Notes of the period, newest first. Long range: only the notes, short range: date + time + subject.
                var notes = Ctx.Sessions.Records.Where(x => !string.IsNullOrEmpty(x.Note))
                    .Where(x => { var t = SessionStore.StartOf(x); return t >= r.Start && t < r.End; })
                    .OrderByDescending(x => x.StartMs).ToList();
                list.EmptyText = "No notes " + PeriodWord() + ". They're added when a pomodoro ends.";
                list.RightText = o =>
                {
                    var n = o as NoteItem;
                    return n == null ? null : SessionStore.StartOf(n.Record).ToString(range == StatRange.Day ? "HH:mm" : "ddd d MMM · HH:mm", CultureInfo.GetCultureInfo("en-GB"));
                };
                list.SetItems(notes.Select(x =>
                {
                    var subj = Planner.Find(Ctx.Settings, x.SubjectId);
                    return (object)new NoteItem { Record = x, Text = (subj != null ? subj.Name + "  —  " : "") + x.Note };
                }));
                lowerHint.Text = notes.Count > 0 ? notes.Count + (notes.Count == 1 ? " note" : " notes") : "";
            }
            else if (tab == 2)
            {
                var top = Insights.Top(Ctx.Distractions, r.Start, r.End, 20);
                int count = Insights.Count(Ctx.Distractions, r.Start, r.End);
                list.EmptyText = "Nothing blocked " + PeriodWord() + ".";
                list.RightText = o =>
                {
                    var d = top.FirstOrDefault(x => x.Name + (x.Kind == "app" ? "  (app)" : "") == (string)o);
                    return d == null ? null : Insights.Describe(d);
                };
                list.SetItems(top.Select(x => (object)(x.Name + (x.Kind == "app" ? "  (app)" : ""))));
                lowerHint.Text = count > 0 ? count + (count == 1 ? " attempt" : " attempts") : "";
            }
            else lowerHint.Text = "";
        }

        string PeriodWord()
        {
            return range == StatRange.Day ? "on this day" : range == StatRange.Week ? "this week" : range == StatRange.Month ? "this month" : "this year";
        }
    }
}
