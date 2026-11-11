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
        readonly Toggle autoBreaks, autoFocus, strict, sound, autostart, closeToTray, minimizeToTray, smartBreaks, askNotes, hotkeys;
        readonly Segmented miniMode;
        readonly FlatButton[] rhythms = new FlatButton[Rhythm.All.Length];
        readonly Label rhythmHint;

        public SettingsPage(TrayContext ctx, Size size) : base(ctx, size)
        {
            Header("Settings", "Tune the timer and how Fokkus behaves.");
            int lx = Pad, lw = S(330);
            int rx = Pad + lw + S(28), rw = Width - Pad - rx;

            Section("Durations", lx, S(98), lw);
            // One-click rhythms; the steppers below still fine-tune them.
            int bw = (lw - 2 * S(8)) / 3;
            for (int i = 0; i < Rhythm.All.Length; i++)
            {
                var r = Rhythm.All[i];
                rhythms[i] = Button(r.Name, null, lx + i * (bw + S(8)), S(124), bw, S(36), (s, e) => ApplyRhythm(r));
                rhythms[i].Font = Theme.F(9.5f);
            }
            rhythmHint = AddLabel("", Theme.F(8.5f), Theme.Muted, lx, S(164), lw, S(22), ContentAlignment.MiddleLeft);
            focus = Row("Focus", lx, S(194), 5, 180, 5, " min", v => Ctx.Settings.FocusMinutes = v);
            shortBreak = Row("Short break", lx, S(236), 1, 30, 1, " min", v => Ctx.Settings.ShortBreakMinutes = v);
            longBreak = Row("Long break", lx, S(278), 5, 60, 5, " min", v => Ctx.Settings.LongBreakMinutes = v);
            sessions = Row("Long break after", lx, S(320), 2, 8, 1, " sessions", v => Ctx.Settings.SessionsBeforeLongBreak = v);

            Section("Flow", lx, S(372), lw);
            autoBreaks = AddToggle("Start breaks automatically", null, lx, S(398), lw, (s, e) => { Ctx.Settings.AutoStartBreaks = autoBreaks.Checked; Save(); });
            autoFocus = AddToggle("Start focus automatically after breaks", null, lx, S(436), lw, (s, e) => { Ctx.Settings.AutoStartFocus = autoFocus.Checked; Save(); });
            strict = AddToggle("Strict mode", "No pause, skip, stop or quitting during focus, and the block lists can't be loosened.", lx, S(474), lw, (s, e) => { Ctx.Settings.StrictMode = strict.Checked; Save(); });
            smartBreaks = AddToggle("Suggest longer breaks", "After a long block (50+ min) or when you skipped breaks, the break pop-up offers a longer one.", lx, S(546), lw,
                (s, e) => { Ctx.Settings.SmartBreaks = smartBreaks.Checked; Save(); });

            Section("Mini timer", rx, S(98), rw);
            miniMode = Place(new Segmented("Always", "In sessions", "Off"), rx, S(124), rw, S(36));
            miniMode.SelectionChanged += (s, e) => { Ctx.Settings.MiniTimerMode = miniMode.SelectedIndex; Save(); };
            AddLabel("Opacity", Theme.F(10), Theme.Text, rx, S(170), S(150), S(34), ContentAlignment.MiddleLeft);
            opacity = AddStepper(rx + rw - S(150), S(170), S(150), 40, 100, 5, " %", (s, e) => { Ctx.Settings.MiniOpacity = opacity.Value; Save(); });

            Section("General", rx, S(224), rw);
            sound = AddToggle("Play a chime when a phase ends", null, rx, S(250), rw, (s, e) => { Ctx.Settings.SoundEnabled = sound.Checked; Save(); });
            autostart = AddToggle("Start with Windows (in the tray)", null, rx, S(288), rw, (s, e) => { Ctx.Settings.StartWithWindows = autostart.Checked; Save(); });
            closeToTray = AddToggle("Closing the window keeps it in the tray", null, rx, S(326), rw, (s, e) => { Ctx.Settings.CloseToTray = closeToTray.Checked; Save(); });
            minimizeToTray = AddToggle("Minimizing hides it in the tray", "Off: minimize to the taskbar like any other window.", rx, S(364), rw,
                (s, e) => { Ctx.Settings.MinimizeToTray = minimizeToTray.Checked; Save(); });

            Section("Notes & shortcuts", rx, S(430), rw);
            askNotes = AddToggle("Ask what you finished after each pomodoro", null, rx, S(456), rw, (s, e) => { Ctx.Settings.AskForNotes = askNotes.Checked; Save(); });
            hotkeys = AddToggle("Global shortcuts", HotkeyText, rx, S(494), rw, (s, e) => { Ctx.Settings.HotkeysEnabled = hotkeys.Checked; Save(); });
        }

        const string HotkeyText = "Ctrl+Alt+F start/pause · Ctrl+Alt+S skip · Ctrl+Alt+T mini timer · Ctrl+Alt+O open Fokkus — from any app.";

        void ApplyRhythm(Rhythm r)
        {
            var s = Ctx.Settings;
            s.FocusMinutes = r.Focus;
            s.ShortBreakMinutes = r.ShortBreak;
            s.LongBreakMinutes = r.LongBreak;
            s.SessionsBeforeLongBreak = r.Sessions;
            Save();
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
            var cur = Rhythm.Matching(s);
            for (int i = 0; i < rhythms.Length; i++)
            {
                if (Rhythm.All[i] == cur) rhythms[i].SetAccent(Theme.Focus);
                else rhythms[i].SetPlain();
            }
            rhythmHint.Text = (cur != null ? "" : "Custom: ") + s.FocusMinutes + " min focus · " + s.ShortBreakMinutes + " min break · " +
                              s.LongBreakMinutes + " min long break after " + s.SessionsBeforeLongBreak +
                              (Ctx.Engine.State != RunState.Idle ? "  (from the next phase)" : "");
            autoBreaks.SetSilently(s.AutoStartBreaks);
            autoFocus.SetSilently(s.AutoStartFocus);
            strict.SetSilently(s.StrictMode);
            strict.Enabled = !locked;
            miniMode.SetSilently(s.MiniTimerMode);
            opacity.SetSilently(s.MiniOpacity);
            sound.SetSilently(s.SoundEnabled);
            autostart.SetSilently(s.StartWithWindows);
            closeToTray.SetSilently(s.CloseToTray);
            minimizeToTray.SetSilently(s.MinimizeToTray);
            smartBreaks.SetSilently(s.SmartBreaks);
            askNotes.SetSilently(s.AskForNotes);
            hotkeys.SetSilently(s.HotkeysEnabled);
            string problem = Ctx.HotkeyProblems;
            hotkeys.Description = problem != null ? "⚠ " + problem : HotkeyText;
            hotkeys.Invalidate();
        }
    }
}
