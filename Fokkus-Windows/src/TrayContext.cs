using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Fokkus
{
    class BlockEntry
    {
        public string Domain;
        public int Count;
        public DateTime Last;
        public override string ToString() { return Domain; }
    }

    // The heart of the app: owns the timer, the blockers, the tray icon and all windows.
    class TrayContext : ApplicationContext
    {
        public readonly AppSettings Settings;
        public readonly SessionStore Sessions;
        public readonly PomodoroEngine Engine;
        public readonly ProxyServer Proxy;
        public readonly AppBlocker AppBlocker;
        public readonly AppGuard AppGuard;
        public readonly DistractionLog Distractions;
        public readonly MusicPlayer Music;
        public readonly SyncService Sync;
        public readonly Dictionary<string, BlockEntry> BlockLog = new Dictionary<string, BlockEntry>();
        Hotkeys hotkeys;
        bool hotkeysOn;
        NotePrompt notePrompt;
        FocusRecord lastRecord;
        int skippedBreaks;
        DateTime nextSync, nextReviewCheck;

        readonly Control invoker;
        readonly NotifyIcon tray;
        readonly MiniTimerForm mini;
        readonly TipToast toast;
        readonly System.Windows.Forms.Timer clock;
        readonly Dictionary<string, DateTime> lastNotice = new Dictionary<string, DateTime>();
        MainForm main;
        Icon trayIcon;
        string trayKey;
        int focusSecondsSinceTip;
        bool blockLogDirty;
        DateTime lastGlobalNotice = DateTime.MinValue;
        string planNote;
        int guardTick;

        public bool IsExiting { get; private set; }

        // Self-test mode (--screenshots): no tray icon, no windows on screen, never touches proxy or apps.
        public static bool Headless;

        public TrayContext(bool startMinimized, EventWaitHandle showSignal)
        {
            invoker = new Control();
            invoker.CreateControl();

            Settings = AppSettings.Load();
            Sessions = SessionStore.Load();
            Sessions.ReadOnly = Headless;
            Engine = new PomodoroEngine(() => Settings);
            Engine.StateChanged += (s, e) => OnStateChanged();
            Engine.PhaseCompleted += OnPhaseCompleted;
            Engine.FocusEnded += OnFocusEnded;
            Engine.Tick += (s, e) => OnEngineTick();

            Proxy = new ProxyServer();
            Proxy.Rules = HostRules.From(Settings);
            Proxy.Blocked += host => { try { invoker.BeginInvoke(new Action(() => OnSiteBlocked(host))); } catch { } };
            Proxy.Start();

            AppBlocker = new AppBlocker(invoker);
            AppBlocker.AppClosed += OnAppClosed;
            AppGuard = new AppGuard();
            AppGuard.AppMinimized += OnAppMinimized;
            Distractions = DistractionLog.Load();
            Distractions.ReadOnly = Headless;
            Music = new MusicPlayer();
            Sync = new SyncService(this);
            nextSync = DateTime.Now.AddSeconds(8);
            nextReviewCheck = DateTime.Now.AddSeconds(20);

            toast = new TipToast();
            mini = new MiniTimerForm(this);

            tray = new NotifyIcon();
            tray.Text = "Fokkus";
            tray.ContextMenuStrip = BuildMenu();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };
            UpdateTray();
            tray.Visible = !Headless;

            clock = new System.Windows.Forms.Timer();
            clock.Interval = 1000;
            clock.Tick += (s, e) => OnClock();
            clock.Start();

            if (Headless) return;
            SystemEvents.SessionEnding += OnSessionEnding;
            // After sleep, the lock screen or a monitor change the mini timer is shown afresh.
            SystemEvents.DisplaySettingsChanged += OnDisplayEvent;
            SystemEvents.PowerModeChanged += OnPowerEvent;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            ApplyAutostart();

            // Ctrl+Alt+F / S / T / O from any app.
            hotkeys = new Hotkeys();
            hotkeys.Add(Keys.F, "Start / pause", ToggleTimer);
            hotkeys.Add(Keys.S, "Skip", Skip);
            hotkeys.Add(Keys.T, "Mini timer", ToggleMini);
            hotkeys.Add(Keys.O, "Open Fokkus", ShowMain);
            hotkeysOn = Settings.HotkeysEnabled;
            hotkeys.Apply(hotkeysOn);

            var t = new Thread(() =>
            {
                while (true)
                {
                    showSignal.WaitOne();
                    try { invoker.BeginInvoke(new Action(ShowMain)); } catch { }
                }
            });
            t.IsBackground = true;
            t.Start();

            OnStateChanged();
            if (!startMinimized) ShowMain();
            else if (Settings.MiniTimerMode != 0)
                Notify("\U0001F512", "Fokkus is running", "It's waiting in the tray. Click the icon to open it.", Theme.Focus, 6, null, null);
        }

        public bool StrictLocked { get { return Settings.StrictMode && Engine.IsLockedIn; } }

        // ------------------------------------------------------------ actions

        public void ToggleTimer()
        {
            if (Engine.State == RunState.Running && StrictLocked) { StrictNotice(); return; }
            if (Engine.State == RunState.Idle) focusSecondsSinceTip = 0;
            Engine.Toggle();
        }

        public void Skip()
        {
            if (StrictLocked) { StrictNotice(); return; }
            // Skipping breaks again and again earns a nudge (and a longer break) next time.
            if (Engine.State != RunState.Idle && Engine.Phase != Phase.Focus) skippedBreaks++;
            Engine.Skip();
        }

        public void Stop()
        {
            if (StrictLocked) { StrictNotice(); return; }
            skippedBreaks = 0;
            Engine.Stop();
        }

        public void ToggleMini()
        {
            if (mini.Visible) { Settings.MiniTimerMode = 2; Settings.Save(); UpdateMiniVisibility(); }
            else SwitchToMini();
        }

        public void OnUi(Action a)
        {
            try { invoker.BeginInvoke(a); } catch (Exception ex) { Log.Error(ex); }
        }

        // Statistics or the plan changed from outside (sync, restore): redraw everything.
        public void DataChanged()
        {
            Engine.SettingsChanged();
            mini.RefreshView();
            if (main != null && !main.IsDisposed) main.RefreshAll();
        }

        public string HotkeyProblems { get { return hotkeys == null ? null : hotkeys.Problems(hotkeysOn); } }

        // ------------------------------------------------------------ focus music

        public void SetMusic(bool on)
        {
            Settings.MusicOn = on;
            SettingsChanged();
            if (on && Engine.State != RunState.Running) mini.Flash("♪ plays when you focus");
        }

        void UpdateMusic()
        {
            if (Headless) return;
            bool play = Settings.MusicOn && Engine.State == RunState.Running && Engine.Phase == Phase.Focus;
            if (play) Music.Play(Stations.Find(Settings, Settings.MusicStation), Settings.MusicVolume);
            else Music.Stop();
        }

        public void FillMusicMenu(ToolStripItemCollection items)
        {
            items.Clear();
            var onItem = new ToolStripMenuItem("Focus music", null, (s, e) => SetMusic(!Settings.MusicOn));
            onItem.Checked = Settings.MusicOn;
            items.Add(onItem);
            items.Add(new ToolStripSeparator());
            foreach (var st in Stations.All(Settings))
            {
                var station = st;
                var it = new ToolStripMenuItem(st.Name + "   ·   " + st.Credit, null, (s, e) =>
                {
                    Settings.MusicStation = station.Id;
                    Settings.MusicOn = true;
                    SettingsChanged();
                });
                it.Checked = Settings.MusicStation == st.Id;
                items.Add(it);
            }
            items.Add(new ToolStripSeparator());
            var vol = new ToolStripMenuItem("Volume");
            foreach (int v in new[] { 10, 20, 30, 40, 60, 80, 100 })
            {
                int value = v;
                var vi = new ToolStripMenuItem(v + " %", null, (s, e) => { Settings.MusicVolume = value; Music.SetVolume(value); Settings.Save(); });
                vi.Checked = Settings.MusicVolume == v;
                vol.DropDownItems.Add(vi);
            }
            DarkSubmenu(vol);
            items.Add(vol);
            items.Add(new ToolStripMenuItem("Add a stream URL…", null, (s, e) =>
            {
                string url = Dialogs.AskText(DialogOwner, "Add a stream", "Link to an internet radio stream (http/https, e.g. ending in .mp3):", "");
                if (url == null) return;
                url = url.Trim();
                if (!Stations.IsStreamUrl(url))
                {
                    Notify("⚠", "That isn't a stream link", "Use a direct http(s) link to an audio stream. YouTube links can't be played here.", Theme.Warn, 8, null, null);
                    return;
                }
                if (!Settings.CustomStations.Any(c => c.Url == url)) Settings.CustomStations.Add(new CustomStation { Name = Stations.HostOf(url), Url = url });
                Settings.MusicStation = "custom:" + url;
                Settings.MusicOn = true;
                SettingsChanged();
            }));
            var cur = Stations.Find(Settings, Settings.MusicStation);
            if (cur.Id.StartsWith("custom:"))
                items.Add(new ToolStripMenuItem("Remove “" + cur.Name + "”", null, (s, e) =>
                {
                    Settings.CustomStations.RemoveAll(c => "custom:" + c.Url == cur.Id);
                    Settings.MusicStation = "lofi";
                    SettingsChanged();
                }));
            if (Music.Error != null && Settings.MusicOn) items.Add(new ToolStripMenuItem(Music.Error) { Enabled = false });
            items.Add(new ToolStripMenuItem("Streams by SomaFM — free, listener-supported radio") { Enabled = false });
        }

        // ------------------------------------------------------------ weekly review

        public void ShowReview(DateTime anyDay)
        {
            var f = new ReviewForm(this, anyDay);
            f.Show();
            f.Activate();
        }

        void CheckWeeklyReview()
        {
            DateTime due = Review.DueWeek(DateTime.Now);
            string key = due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(Settings.LastReviewedWeek ?? "", key) >= 0) return;
            if (Engine.IsLockedIn) return;   // wait until you're not focusing
            Settings.LastReviewedWeek = key;
            Settings.Save();
            var r = Analytics.Compute(Settings, Sessions, StatRange.Week, due);
            if (r.TotalMinutes < 1) return;   // nothing to look back on
            Notify("\U0001F4CA", "Your week in review", Theme.Compact(r.TotalMinutes) + " focused, " + r.Pomodoros + " pomodoros" +
                   (r.Planned > 0 ? " of " + r.Planned + " planned" : "") + ". See what went well and what to plan next.",
                Theme.Focus, 30, "Open review", () => ShowReview(due));
        }

        // ------------------------------------------------------------ notes

        void AskForNote(FocusRecord record)
        {
            if (Headless || record == null) return;
            if (notePrompt == null) notePrompt = new NotePrompt();
            var subj = Planner.Find(Settings, record.SubjectId);
            string sub = (subj != null ? subj.Name + " · " : "") + Math.Round(record.Seconds / 60.0) + " min · " + SessionStore.StartOf(record).ToString("HH:mm");
            Rectangle avoid = toast.Visible ? toast.Bounds : (mini.Visible ? mini.Bounds : Rectangle.Empty);
            notePrompt.Ask(sub, avoid, text =>
            {
                Sessions.SetNote(record, text);
                mini.Flash("✎ note saved");
                if (main != null && !main.IsDisposed) main.RefreshPage<StatsPage>();
                ScheduleSync(5);
            });
        }

        // ------------------------------------------------------------ sync

        void ScheduleSync(int seconds)
        {
            DateTime at = DateTime.Now.AddSeconds(seconds);
            if (at < nextSync) nextSync = at;
        }

        void StrictNotice()
        {
            Notify("\U0001F512", "Strict mode is on", "No escape until this focus session ends — " + Theme.FormatTime(Engine.Remaining) + " to go. You've got this.", Theme.Focus, 6, null, null);
        }

        // Turns one of the four functions on or off (also mid-session). Turning one off is refused in strict mode.
        public bool SetFilter(Filter f, bool on)
        {
            if (Settings.IsOn(f) == on) return true;
            if (!on && StrictLocked) { StrictNotice(); return false; }
            Settings.SetOn(f, on);
            SettingsChanged();
            return true;
        }

        // Strict mode only lets you make the rules stricter until the focus session ends.
        public bool RefuseLoosening(bool loosens)
        {
            if (!loosens || !StrictLocked) return false;
            Notify("\U0001F512", "Strict mode is on", "You can only make your lists stricter until this focus session ends — " +
                   Theme.FormatTime(Engine.Remaining) + " to go.", Theme.Focus, 5, null, null);
            return true;
        }

        // ------------------------------------------------------------ profiles

        // Switches to a profile's four functions + strict mode. In strict mode a profile that loosens the lock is refused.
        public bool ApplyProfile(Profile p, bool automatic)
        {
            if (p == null) return false;
            if (StrictLocked && p.Loosens(Settings))
            {
                if (!automatic) StrictNotice();
                return false;
            }
            bool changed = !p.Matches(Settings) || Settings.ActiveProfileId != p.Id;
            p.ApplyTo(Settings);
            if (changed) SettingsChanged();
            if (!Headless) mini.Flash(p.Name + " profile");
            return true;
        }

        // A subject can switch on its own profile when you pick it (e.g. Programming -> "Programming").
        void ApplySubjectProfile(Subject s)
        {
            var p = s == null ? null : Settings.FindProfile(s.ProfileId);
            if (p != null && !p.Matches(Settings)) ApplyProfile(p, true);
        }

        public string ProfileLabel
        {
            get { var p = Settings.CurrentProfile(); return p != null ? p.Name : "Custom"; }
        }

        IWin32Window DialogOwner { get { return main != null && !main.IsDisposed && main.Visible ? main : null; } }

        // Menu: pick a profile, save the current switches as one, update / rename / delete, and link profiles to subjects.
        public void FillProfileMenu(ToolStripItemCollection items)
        {
            items.Clear();
            var cur = Settings.CurrentProfile();
            foreach (var p in Settings.Profiles)
            {
                var prof = p;
                var it = new ToolStripMenuItem(p.Name, null, (s, e) => ApplyProfile(prof, false));
                it.Checked = p == cur;
                it.ToolTipText = Describe(p);
                items.Add(it);
            }
            items.Add(new ToolStripSeparator());
            items.Add(new ToolStripMenuItem("Save current switches as a new profile…", null, (s, e) =>
            {
                string name = Dialogs.AskText(DialogOwner, "New profile", "Name for the current switches:", "");
                if (name == null) return;
                var p = new Profile { Name = name };
                p.CaptureFrom(Settings);
                Settings.Profiles.Add(p);
                Settings.ActiveProfileId = p.Id;
                SettingsChanged();
            }));
            var last = Settings.FindProfile(Settings.ActiveProfileId);
            if (last != null)
            {
                var upd = new ToolStripMenuItem("Update “" + last.Name + "” to the current switches", null, (s, e) => { last.CaptureFrom(Settings); SettingsChanged(); });
                upd.Enabled = !last.Matches(Settings);
                items.Add(upd);
                items.Add(new ToolStripMenuItem("Rename “" + last.Name + "”…", null, (s, e) =>
                {
                    string name = Dialogs.AskText(DialogOwner, "Rename profile", "New name:", last.Name);
                    if (name != null) { last.Name = name; SettingsChanged(); }
                }));
                var del = new ToolStripMenuItem("Delete “" + last.Name + "”", null, (s, e) =>
                {
                    Settings.Profiles.Remove(last);
                    foreach (var subj in Settings.Subjects) if (subj.ProfileId == last.Id) subj.ProfileId = null;
                    Settings.ActiveProfileId = null;
                    SettingsChanged();
                });
                del.Enabled = Settings.Profiles.Count > 1;
                items.Add(del);
            }
            items.Add(new ToolStripSeparator());
            var link = new ToolStripMenuItem("Switch on automatically for a subject");
            if (Settings.Subjects.Count == 0)
            {
                link.Text = "Switch on automatically for a subject (add subjects first)";
                link.Enabled = false;
            }
            foreach (var subj in Settings.Subjects)
            {
                var sj = subj;
                var linked = Settings.FindProfile(subj.ProfileId);
                var sub = new ToolStripMenuItem(subj.Name + (linked != null ? "  →  " + linked.Name : ""));
                var none = new ToolStripMenuItem("No profile", null, (s, e) => { sj.ProfileId = null; SettingsChanged(); });
                none.Checked = linked == null;
                sub.DropDownItems.Add(none);
                foreach (var p in Settings.Profiles)
                {
                    var prof = p;
                    var pi = new ToolStripMenuItem(p.Name, null, (s, e) =>
                    {
                        sj.ProfileId = prof.Id;
                        SettingsChanged();
                        if (Settings.ActiveSubjectId == sj.Id) ApplyProfile(prof, false);
                    });
                    pi.Checked = linked == p;
                    sub.DropDownItems.Add(pi);
                }
                DarkSubmenu(sub);
                link.DropDownItems.Add(sub);
            }
            DarkSubmenu(link);
            items.Add(link);
        }

        // Submenus don't inherit the dark renderer by themselves.
        public static void DarkSubmenu(ToolStripMenuItem m)
        {
            m.DropDown.Renderer = new DarkMenuRenderer();
            m.DropDown.Font = Theme.F(9.5f);
            var dd = m.DropDown as ToolStripDropDownMenu;
            if (dd != null) { dd.ShowImageMargin = false; dd.ShowCheckMargin = true; }
        }

        public static string Describe(Profile p)
        {
            var on = new List<string>();
            if (p.AllowSitesOn) on.Add("allowed websites");
            if (p.BlockSitesOn) on.Add("blocked websites");
            if (p.AllowAppsOn) on.Add("allowed apps");
            if (p.BlockAppsOn) on.Add("blocked apps");
            return (on.Count == 0 ? "No blocking" : "On: " + string.Join(", ", on)) + (p.Strict ? " · strict mode" : "");
        }

        public static string FilterName(Filter f)
        {
            switch (f)
            {
                case Filter.AllowSites: return "Allowed websites";
                case Filter.BlockSites: return "Blocked websites";
                case Filter.AllowApps: return "Allowed apps";
                default: return "Blocked apps";
            }
        }

        public void SettingsChanged()
        {
            Settings.Save();
            Engine.SettingsChanged();
            mini.ApplyOpacity();
            ApplyAutostart();
            if (hotkeys != null && hotkeysOn != Settings.HotkeysEnabled)
            {
                hotkeysOn = Settings.HotkeysEnabled;
                hotkeys.Apply(hotkeysOn);
            }
            if (main != null && !main.IsDisposed) main.RefreshAll();
        }

        public void ShowMain()
        {
            if (main == null || main.IsDisposed) main = new MainForm(this);
            main.RefreshAll();
            if (!main.Visible) main.Show();
            if (main.WindowState == FormWindowState.Minimized) main.WindowState = FormWindowState.Normal;
            main.Activate();
            main.BringToFront();
            UpdateMiniVisibility();
        }

        public void SwitchToMini()
        {
            if (Settings.MiniTimerMode == 2) { Settings.MiniTimerMode = 0; Settings.Save(); }
            if (main != null && main.Visible) main.Hide();
            UpdateMiniVisibility();
            if (!mini.Visible) mini.Show();
        }

        public void OnMainHidden()
        {
            UpdateMiniVisibility();
        }

        public void ShowTipNow()
        {
            ShowTip(Tips.Next(Engine.Phase == Phase.Focus ? TipKind.Micro : TipKind.Break, Settings.DisabledTipCategories));
        }

        public void RestoreInternet()
        {
            SystemProxy.RecoverAll();
            UpdateBlocking();
            Notify("✅", "Internet settings restored", Engine.IsLockedIn && HostRules.From(Settings).Active
                ? "Your focus session is still running, so filtering was switched back on."
                : "Your proxy settings are back to how they were before Fokkus.", Theme.ShortBreak, 6, null, null);
        }

        public void ClearBlockLog() { BlockLog.Clear(); }
        public void RemoveFromBlockLog(string domain) { BlockLog.Remove(domain); }

        public void Exit()
        {
            if (StrictLocked) { StrictNotice(); return; }
            IsExiting = true;
            clock.Stop();
            if (!Headless) try { SystemProxy.Disengage(); } catch (Exception ex) { Log.Error(ex); }
            AppBlocker.Update(false, new string[0]);
            AppGuard.Update(false, new string[0]);
            Music.Dispose();
            if (hotkeys != null) hotkeys.Dispose();
            Distractions.SaveIfDirty();
            if (notePrompt != null) notePrompt.Dispose();
            Proxy.Stop();
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.DisplaySettingsChanged -= OnDisplayEvent;
            SystemEvents.PowerModeChanged -= OnPowerEvent;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            tray.Visible = false;
            tray.Dispose();
            if (main != null && !main.IsDisposed) main.Close();
            mini.Close();
            toast.Close();
            ExitThread();
        }

        void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            try { SystemProxy.Disengage(); } catch { }
        }

        void OnDisplayEvent(object sender, EventArgs e) { ReviveMiniSoon(); }
        void OnPowerEvent(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) ReviveMiniSoon(); }
        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
                ReviveMiniSoon();
        }

        // Waits a moment for the displays to settle, then shows the mini timer afresh.
        void ReviveMiniSoon()
        {
            try
            {
                invoker.BeginInvoke(new Action(() =>
                {
                    var t = new System.Windows.Forms.Timer();
                    t.Interval = 1500;
                    t.Tick += (s, e) => { t.Stop(); t.Dispose(); if (!IsExiting) mini.Revive(); };
                    t.Start();
                }));
            }
            catch { }
        }

        // ------------------------------------------------------------ engine events

        void OnStateChanged()
        {
            UpdateBlocking();
            UpdateMusic();
            UpdateMiniVisibility();
            UpdateTray();
            mini.RefreshView();
            if (main != null && !main.IsDisposed && main.Visible) main.RefreshAll();
        }

        void OnEngineTick()
        {
            if (mini.Visible) mini.RefreshView();
            if (main != null && !main.IsDisposed) main.OnTick();
        }

        // Logs focused time; after a full pomodoro, moves on to the next subject of today's plan if this one is done.
        void OnFocusEnded(object sender, FocusEndedArgs e)
        {
            planNote = null;
            lastRecord = new FocusRecord
            {
                StartMs = SessionStore.ToUnixMs(e.Start),
                Seconds = e.Seconds,
                SubjectId = Planner.Active(Settings) != null ? Settings.ActiveSubjectId : null,
                Pomodoros = e.Completed ? 1 : 0,
            };
            Sessions.Add(lastRecord);
            ScheduleSync(5);
            if (!e.Completed) return;
            var cur = Planner.Active(Settings);
            if (cur == null) return;
            int target = Planner.Target(cur, DateTime.Today), done = Sessions.PomodorosOn(DateTime.Today, cur.Id);
            if (target <= 0 || done < target) return;
            var next = Planner.NextPending(Settings, Sessions, null);
            if (next == null) planNote = "✅ " + cur.Name + " done — today's whole plan is complete!";
            else if (Settings.AutoNextSubject)
            {
                Settings.ActiveSubjectId = next.Id;
                if (!Headless) Settings.Save();
                planNote = "✅ " + cur.Name + " done for today. Next up: " + next.Name + ".";
                // Its profile (if any) is switched on after the engine has finished this phase change.
                try { invoker.BeginInvoke(new Action(() => ApplySubjectProfile(next))); } catch { }
            }
            else planNote = "✅ " + cur.Name + " done for today. " + next.Name + " is still open.";
        }

        public void SelectSubject(string id)
        {
            Settings.ActiveSubjectId = Planner.Find(Settings, id) != null ? id : null;
            Settings.Save();
            mini.RefreshView();
            if (main != null && !main.IsDisposed) main.RefreshAll();
            ApplySubjectProfile(Planner.Active(Settings));
        }

        public void PlanChanged()
        {
            mini.RefreshView();
            if (main != null && !main.IsDisposed) main.RefreshPage<TimerPage>();
        }

        public void OpenPage(int index)
        {
            ShowMain();
            main.Navigate(index);
        }

        void OnPhaseCompleted(object sender, PhaseEventArgs e)
        {
            if (e.To == Phase.Focus) focusSecondsSinceTip = 0;
            if (!e.Natural) return;
            if (e.From != Phase.Focus) skippedBreaks = 0;   // a break taken in full
            if (Settings.SoundEnabled) Chime.Play();

            int minutes = (int)Engine.DurationOf(e.To).TotalMinutes;
            if (e.To == Phase.Focus)
            {
                if (e.AutoStarted)
                    Notify("\U0001F512", "Back to focus", minutes + " minutes. Distractions are blocked again — lock in!", Theme.Focus, 8, null, null);
                else
                    Notify("⏰", "Break's over", "Ready for the next " + minutes + "-minute focus session?", Theme.Focus, 30, "Start focus", () => Engine.Start());
            }
            else
            {
                bool longBreak = e.To == Phase.LongBreak;
                Color c = Theme.PhaseColor(e.To);
                string head = longBreak ? "Cycle complete — long break!" : "Focus done — nice work!";
                string body = (e.AutoStarted ? "Your " : "Time for a ") + minutes + "-minute " + (longBreak ? "long " : "") + "break" + (e.AutoStarted ? " has started." : ".");
                string icon = "\U0001F389";
                if (Settings.BreakSuggestions)
                {
                    var tip = Tips.Next(longBreak ? TipKind.LongBreak : TipKind.Break, Settings.DisabledTipCategories);
                    body += "\n" + Tips.Icon(tip.Category) + " " + tip.Title + ": " + tip.Text;
                }
                if (planNote != null) body = planNote + "\n" + body;
                string actionText = e.AutoStarted ? null : "Start break";
                Action action = e.AutoStarted ? (Action)null : () => Engine.Start();
                // Smarter breaks: a longer one after a long block or after skipping breaks.
                int longer = Settings.SmartBreaks && e.From == Phase.Focus ? Breaks.Suggest(e.FocusMinutes, minutes, skippedBreaks) : 0;
                if (longer > 0)
                {
                    bool auto = e.AutoStarted;
                    body = Breaks.Reason(e.FocusMinutes, skippedBreaks) + " Give your brain " + longer + " minutes.\n" + body;
                    actionText = auto ? "Make it " + longer + " min" : "Start " + longer + "-min break";
                    action = () =>
                    {
                        Engine.SetPhaseLength(TimeSpan.FromMinutes(longer));
                        if (!auto) Engine.Start();
                    };
                }
                Notify(icon, head, body, c, e.AutoStarted ? 20 : 30, actionText, action);
                if (e.From == Phase.Focus && Settings.AskForNotes && lastRecord != null && lastRecord.Pomodoros > 0) AskForNote(lastRecord);
            }
        }

        void OnClock()
        {
            UpdateTray();
            if (!Headless && ++guardTick % 2 == 0) mini.Guard();
            if (blockLogDirty)
            {
                blockLogDirty = false;
                if (main != null && !main.IsDisposed && main.Visible) main.RefreshPage<WebsitesPage>();
            }
            if (Engine.State == RunState.Running && Engine.Phase == Phase.Focus)
            {
                focusSecondsSinceTip++;
                if (Settings.TipsEnabled && focusSecondsSinceTip >= Settings.TipIntervalMinutes * 60)
                {
                    focusSecondsSinceTip = 0;
                    if (Engine.Remaining.TotalSeconds > 90)
                        ShowTip(Tips.Next(TipKind.Micro, Settings.DisabledTipCategories));
                }
            }
            if (Engine.IsLockedIn) Proxy.StatusText = Theme.FormatTime(Engine.Remaining) + " of focus left — you've got this.";
            if (Headless) return;

            if (guardTick % 3 == 0)
            {
                bool hadError = Music.Error != null;
                Music.Check();
                if (hadError != (Music.Error != null) && main != null && !main.IsDisposed) main.RefreshPage<TimerPage>();
            }
            if (guardTick % 30 == 0) Distractions.SaveIfDirty();
            if (!string.IsNullOrEmpty(Settings.SyncFolder) && DateTime.Now >= nextSync)
            {
                nextSync = DateTime.Now.AddMinutes(10);
                Sync.Run();
            }
            if (DateTime.Now >= nextReviewCheck)
            {
                nextReviewCheck = DateTime.Now.AddMinutes(1);
                CheckWeeklyReview();
            }
        }

        // ------------------------------------------------------------ blocking

        void UpdateBlocking()
        {
            bool locked = Engine.IsLockedIn;
            var rules = HostRules.From(Settings);
            Proxy.Rules = rules;
            if (Headless) return;
            try
            {
                if (locked && rules.Active) SystemProxy.Engage(Proxy.Port, rules.BuildBypassList());
                else SystemProxy.Disengage();
            }
            catch (Exception ex) { Log.Error(ex); }
            // Blocked apps are closed; with "Allowed apps" on, every other app is minimized. Blocked wins.
            AppBlocker.Update(locked && Settings.BlockAppsOn, Settings.BlockedApps);
            AppGuard.Update(locked && Settings.AllowAppsOn, Settings.AllowedApps.Where(a => !(Settings.BlockAppsOn && AppSettings.Has(Settings.BlockedApps, a))));
        }

        void OnSiteBlocked(string host)
        {
            string domain = HostRules.BaseDomain(host);
            BlockEntry be;
            if (!BlockLog.TryGetValue(domain, out be))
            {
                be = new BlockEntry();
                be.Domain = domain;
                BlockLog[domain] = be;
                if (BlockLog.Count > 300)
                {
                    var oldest = BlockLog.Values.OrderBy(x => x.Last).First();
                    BlockLog.Remove(oldest.Domain);
                }
            }
            be.Count++;
            be.Last = DateTime.Now;
            blockLogDirty = true;

            // Pages pull in lots of background hosts; only speak up for real distractions.
            var rules = Proxy.Rules;
            bool distraction = (rules.BlockOn && rules.MatchesBlocked(host)) || Presets.IsKnownDistraction(host);
            if (!distraction) return;
            Distractions.Add("site", domain, DateTime.Now);
            mini.Flash("✕ " + domain);
            if (CanNotice("site:" + domain, 90))
                Notify("\U0001F6AB", domain + " is blocked", "Stay locked in — " + Theme.FormatTime(Engine.Remaining) + " left in this focus session.", Theme.Focus, 6, null, null);
        }

        void OnAppClosed(string name)
        {
            Distractions.Add("app", name, DateTime.Now);
            mini.Flash("✕ " + name + " closed");
            if (CanNotice("app:" + name.ToLowerInvariant(), 120))
                Notify("\U0001F6AB", name + " was closed", "It's on your blocked apps list. You can open it again during the break.", Theme.Focus, 6, null, null);
        }

        void OnAppMinimized(string name)
        {
            Distractions.Add("app", name, DateTime.Now);
            mini.Flash("– " + name + " minimized");
            if (CanNotice("min:" + name.ToLowerInvariant(), 120))
                Notify("\U0001F4A4", name + " was minimized", "It isn't on your allowed apps list, so it waits until the break. Nothing was closed.",
                    Theme.Focus, 8, "Allow " + name, () => AllowApp(name));
        }

        // Puts an app on the allowed list (from the "minimized" notice).
        public void AllowApp(string name)
        {
            if (RefuseLoosening(true)) return;
            if (!AppSettings.Has(Settings.AllowedApps, name)) Settings.AllowedApps.Add(name);
            SettingsChanged();
        }

        bool CanNotice(string key, int seconds)
        {
            DateTime last;
            if (lastNotice.TryGetValue(key, out last) && (DateTime.Now - last).TotalSeconds < seconds) return false;
            if ((DateTime.Now - lastGlobalNotice).TotalSeconds < 8) return false;
            lastNotice[key] = DateTime.Now;
            lastGlobalNotice = DateTime.Now;
            return true;
        }

        // ------------------------------------------------------------ UI helpers

        public void Notify(string icon, string title, string body, Color accent, int seconds, string actionText, Action action)
        {
            if (Headless) return;
            toast.ShowToast(icon, title, body, accent, seconds, actionText, action, mini.Visible ? mini.Bounds : Rectangle.Empty);
        }

        void ShowTip(Tip tip)
        {
            Notify(Tips.Icon(tip.Category), tip.Title, tip.Text, Tips.CategoryColor(tip.Category), 15, null, null);
        }

        void UpdateMiniVisibility()
        {
            if (IsExiting || Headless) return;
            bool show = Settings.MiniTimerMode == 0 || (Settings.MiniTimerMode == 1 && Engine.InSession);
            if (show && !mini.Visible) mini.Show();
            else if (!show && mini.Visible) mini.Hide();
        }

        void UpdateTray()
        {
            if (IsExiting) return;
            var st = Engine.State;
            Color c = st == RunState.Idle ? Theme.Idle : Theme.PhaseColor(Engine.Phase);
            if (st == RunState.Paused || st == RunState.Ready) c = Theme.Blend(c, Theme.Idle, 0.5f);
            float rem = st == RunState.Idle ? 1f : (float)(1 - Engine.Progress);
            int bucket = (int)Math.Round(rem * 40);
            string key = c.ToArgb() + "|" + bucket + "|" + Engine.IsLockedIn + "|" + st;
            if (key != trayKey)
            {
                var old = trayIcon;
                trayIcon = IconFactory.TrayIcon(c, bucket / 40f, Engine.IsLockedIn, st);
                tray.Icon = trayIcon;
                if (old != null) old.Dispose();
                trayKey = key;
            }
            string text = "Fokkus";
            if (st != RunState.Idle)
            {
                text += " · " + Theme.PhaseName(Engine.Phase) + " " + Theme.FormatTime(Engine.Remaining);
                if (st == RunState.Paused) text += " (paused)";
                if (st == RunState.Ready) text += " (ready)";
            }
            if (text.Length > 63) text = text.Substring(0, 63);
            if (tray.Text != text) tray.Text = text;
        }

        void ApplyAutostart()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (k == null) return;
                    if (k.GetValue("kk-Lock-in") != null) k.DeleteValue("kk-Lock-in", false); // old name
                    if (Settings.StartWithWindows) k.SetValue("Fokkus", "\"" + Application.ExecutablePath + "\" --minimized");
                    else if (k.GetValue("Fokkus") != null) k.DeleteValue("Fokkus", false);
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // Renders every screen to PNG files without showing anything (used to verify the UI).
        public void CaptureScreens(string dir)
        {
            System.IO.Directory.CreateDirectory(dir);
            var form = new MainForm(this);
            form.StartPosition = FormStartPosition.Manual;
            form.ShowInTaskbar = false;
            form.Location = new Point(-32000, -32000);
            form.Show();
            Application.DoEvents();
            Action<string> shot = name => { using (var bmp = form.Snapshot()) bmp.Save(System.IO.Path.Combine(dir, name)); };

            // Empty state first (a fresh install), then demo data that only lives in memory.
            form.Navigate(1);
            shot("win-plan-empty.png");
            SeedDemoData();
            form.RefreshAll();
            string[] names = { "timer", "plan", "stats", "websites", "apps", "tips", "data", "settings" };
            for (int i = 0; i < names.Length; i++)
            {
                form.Navigate(i);
                shot("win-" + (i + 1) + "-" + names[i] + ".png");
            }
            form.Navigate(2);
            var stats = form.Page<StatsPage>();
            stats.ShowRange(StatRange.Month); shot("win-stats-month.png");
            stats.ShowRange(StatRange.Year); shot("win-stats-year.png");
            stats.ShowRange(StatRange.Day); shot("win-stats-day.png");
            stats.ShowRange(StatRange.Week);
            stats.DebugHover(2);
            shot("win-stats-hover.png");
            stats.ShowLower(1); shot("win-stats-notes.png");
            stats.ShowLower(2); shot("win-stats-distractions.png");
            stats.ShowLower(0);
            using (var review = new ReviewForm(this, DateTime.Today.AddDays(-7))) SaveOffscreen(review, dir, "win-review.png");
            using (var np = new NotePrompt())
            {
                np.Fill("Physics · 25 min · 14:05");
                SaveOffscreen(np, dir, "win-note-prompt.png");
            }
            SaveMini(dir, "win-mini-idle.png");

            Engine.Start();
            Engine.DebugSetRemaining(TimeSpan.FromSeconds(17 * 60 + 42));
            OnSiteBlocked("www.youtube.com");
            OnSiteBlocked("i.ytimg.com");
            OnSiteBlocked("static.cdn-example.net");
            form.RefreshAll();
            form.Navigate(0);
            form.OnTick();
            shot("win-timer-focus.png");
            form.Navigate(3);
            shot("win-websites-blocked.png");
            SaveMini(dir, "win-mini-focus.png");
            mini.Flash("✕ youtube.com");
            SaveMini(dir, "win-mini-flash.png");

            Engine.Skip();
            Engine.DebugSetRemaining(TimeSpan.FromSeconds(3 * 60 + 5));
            SaveMini(dir, "win-mini-break.png");

            var tip = Tips.Next(TipKind.Micro, null);
            toast.Prepare(Tips.Icon("Stretching"), "Neck stretch", "Slowly tilt your ear toward your shoulder for 10 seconds, then switch sides.",
                Tips.CategoryColor("Stretching"), null, null, Rectangle.Empty);
            SaveForm(toast, dir, "win-toast-tip.png");
            toast.Prepare("⏰", "Break's over", "Ready for the next 25-minute focus session?", Theme.Focus, "Start focus", null, Rectangle.Empty);
            SaveForm(toast, dir, "win-toast-action.png");
            form.Dispose();
        }

        // Demo plan + ~8 weeks of history for the self-test screenshots (never saved).
        void SeedDemoData()
        {
            Settings.Subjects.Clear();
            string[] subj = { "Analysis 1", "Physics", "Programming", "Linear Algebra" };
            int[][] targets = { new[] { 3, 2, 3, 2, 3, 1, 0 }, new[] { 2, 2, 0, 2, 2, 0, 1 }, new[] { 2, 0, 3, 2, 0, 2, 0 }, new[] { 0, 2, 1, 0, 2, 0, 0 } };
            for (int i = 0; i < subj.Length; i++)
            {
                var s = new Subject();
                s.Name = subj[i];
                s.ColorIndex = i;
                s.Targets = targets[i].ToList();
                Settings.Subjects.Add(s);
            }
            Settings.ActiveSubjectId = Settings.Subjects[1].Id;
            Settings.Subjects[0].Deadline = DateTime.Today.AddDays(5).ToString("yyyy-MM-dd");
            Settings.Subjects[0].DeadlineLabel = "Exam";
            Settings.Subjects[3].Deadline = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd");
            Settings.Subjects[3].DeadlineLabel = "Project";
            Settings.Subjects[2].ProfileId = "coding";
            Settings.PlanSince = DateTime.Today.AddDays(-56).ToString("yyyy-MM-dd");
            var rnd = new Random(7);
            for (int back = 56; back >= 0; back--)
            {
                var day = DateTime.Today.AddDays(-back);
                var start = day.AddHours(9);
                foreach (var s in Settings.Subjects)
                {
                    int t = Planner.Target(s, day);
                    int done = back == 0 ? (s == Settings.Subjects[0] ? t : s == Settings.Subjects[1] ? 1 : 0) : Math.Max(0, t - rnd.Next(0, 2) + rnd.Next(0, 2));
                    for (int k = 0; k < done; k++)
                    {
                        Sessions.Add(new FocusRecord { StartMs = SessionStore.ToUnixMs(start), Seconds = Settings.FocusMinutes * 60, SubjectId = s.Id, Pomodoros = 1 });
                        start = start.AddMinutes(Settings.FocusMinutes + (k % 4 == 3 ? 15 : 5));
                    }
                }
                if (back % 6 == 2) Sessions.Add(new FocusRecord { StartMs = SessionStore.ToUnixMs(start), Seconds = 14 * 60, Pomodoros = 0 });
            }
            // Notes, distractions, music and a sync folder for the new screens.
            string[] notes = { "Exercise sheet 3, tasks 1–2", "Lecture 7 summary", "Recursion exercises", "Kinematics problems", "Eigenvalues: 4 examples", "Mock exam, part A" };
            var week = Analytics.WeekStart(DateTime.Today);
            int ni = 0;
            foreach (var r in Sessions.Records.Where(x => x.Pomodoros > 0 && SessionStore.StartOf(x) >= week.AddDays(-7)).Take(9))
            {
                r.Note = notes[ni++ % notes.Length];
                r.NoteMs = 1;
            }
            string[] tried = { "youtube.com", "instagram.com", "youtube.com", "reddit.com", "youtube.com", "Discord", "netflix.com", "youtube.com" };
            for (int i = 0; i < 16; i++)
                Distractions.Add(tried[i % tried.Length] == "Discord" ? "app" : "site", tried[i % tried.Length], week.AddDays(-7).AddHours(9 + i * 9).AddMinutes(i));
            for (int i = 0; i < 9; i++)
                Distractions.Add(tried[i % tried.Length] == "Discord" ? "app" : "site", tried[i % tried.Length], week.AddHours(13 + i * 5).AddMinutes(i));
            Settings.MusicOn = true;
            Settings.SyncFolder = @"G:\My Drive";
        }

        // Forms with child controls only paint once shown: show them far off-screen, capture, hide.
        static void SaveOffscreen(Form f, string dir, string name)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.ShowInTaskbar = false;
            f.Location = new Point(-32000, -32000);
            f.Show();
            Application.DoEvents();
            f.Location = new Point(-32000, -32000);
            Application.DoEvents();
            SaveForm(f, dir, name);
            f.Hide();
        }

        void SaveMini(string dir, string name)
        {
            mini.RefreshView();
            SaveForm(mini, dir, name);
        }

        static void SaveForm(Form f, string dir, string name)
        {
            var h = f.Handle;
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(System.IO.Path.Combine(dir, name));
            }
        }

        public ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();
            m.Renderer = new DarkMenuRenderer();
            m.ShowImageMargin = false;
            m.ShowCheckMargin = true;
            m.Font = Theme.F(9.5f);
            var status = new ToolStripMenuItem("") { Enabled = false };
            var toggle = new ToolStripMenuItem("Start", null, (s, e) => ToggleTimer());
            var skip = new ToolStripMenuItem("Skip to next phase", null, (s, e) => Skip());
            var stop = new ToolStripMenuItem("Stop session", null, (s, e) => Stop());
            var miniItem = new ToolStripMenuItem("Show mini timer", null, (s, e) =>
            {
                if (mini.Visible) { Settings.MiniTimerMode = 2; Settings.Save(); UpdateMiniVisibility(); }
                else SwitchToMini();
            });
            var tip = new ToolStripMenuItem("Show a tip", null, (s, e) => ShowTipNow());
            var open = new ToolStripMenuItem("Open Fokkus", null, (s, e) => ShowMain());
            open.Font = Theme.FB(9.5f);
            var quit = new ToolStripMenuItem("Quit", null, (s, e) => Exit());
            var filters = new[] { Filter.AllowSites, Filter.BlockSites, Filter.AllowApps, Filter.BlockApps };
            var filterItems = filters.Select(f => new ToolStripMenuItem(FilterName(f), null, (s, e) => SetFilter(f, !Settings.IsOn(f)))).ToArray();
            var during = new ToolStripMenuItem("During focus") { Enabled = false };
            var profileItem = new ToolStripMenuItem("Profile");
            profileItem.DropDownItems.Add("…");
            DarkSubmenu(profileItem);
            profileItem.DropDownOpening += (s, e) => FillProfileMenu(profileItem.DropDownItems);
            m.Items.AddRange(new ToolStripItem[] { status, new ToolStripSeparator(), toggle, skip, stop, new ToolStripSeparator(), profileItem, during });
            m.Items.AddRange(filterItems);
            var musicItem = new ToolStripMenuItem("Focus music");
            musicItem.DropDownItems.Add("…");
            DarkSubmenu(musicItem);
            musicItem.DropDownOpening += (s, e) => FillMusicMenu(musicItem.DropDownItems);
            var reviewItem = new ToolStripMenuItem("Weekly review", null, (s, e) => ShowReview(DateTime.Today));
            m.Items.AddRange(new ToolStripItem[] { new ToolStripSeparator(), musicItem, reviewItem, miniItem, tip, open, new ToolStripSeparator(), quit });
            m.Opening += (s, e) =>
            {
                var st = Engine.State;
                status.Text = st == RunState.Idle ? "Not running" : Theme.PhaseName(Engine.Phase) + " · " + Theme.FormatTime(Engine.Remaining) + (st == RunState.Paused ? " (paused)" : "");
                toggle.Text = st == RunState.Running ? "Pause" : st == RunState.Paused ? "Resume" : st == RunState.Ready ? "Start next phase" : "Lock in (start focus)";
                toggle.Enabled = !(st == RunState.Running && StrictLocked);
                skip.Enabled = st != RunState.Idle && !StrictLocked;
                stop.Enabled = st != RunState.Idle && !StrictLocked;
                miniItem.Checked = mini.Visible;
                for (int i = 0; i < filters.Length; i++) filterItems[i].Checked = Settings.IsOn(filters[i]);
                profileItem.Text = "Profile: " + ProfileLabel;
                musicItem.Text = "Focus music: " + (Settings.MusicOn ? Stations.Find(Settings, Settings.MusicStation).Name : "off");
                quit.Enabled = !StrictLocked;
                quit.Text = StrictLocked ? "Quit (locked until focus ends)" : "Quit";
            };
            return m;
        }
    }
}
