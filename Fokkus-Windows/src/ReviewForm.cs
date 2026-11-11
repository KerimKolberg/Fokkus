using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace Fokkus
{
    // The weekly review: time vs plan, per subject, your best hours, notes and the distractions you resisted.
    class ReviewForm : Form
    {
        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");
        readonly Panel body;
        int y;

        static int S(float v) { return Theme.S(v); }

        public ReviewForm(TrayContext ctx, DateTime anyDay)
        {
            var w = Review.Compute(ctx.Settings, ctx.Sessions, ctx.Distractions, anyDay);
            var r = w.Stats;
            Text = "Fokkus · Weekly review";
            Icon = IconFactory.AppIcon(32);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(10);
            ClientSize = new Size(S(620), S(640));
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            body = new DoubleBufferedPanel();
            body.BackColor = Theme.Bg;
            body.AutoScroll = true;
            body.SetBounds(0, 0, ClientSize.Width, ClientSize.Height - S(64));
            Controls.Add(body);
            var close = new FlatButton("Close");
            close.SetAccent(Theme.Focus);
            close.SetBounds(ClientSize.Width - S(32) - S(120), ClientSize.Height - S(52), S(120), S(38));
            close.Click += (s, e) => Close();
            Controls.Add(close);

            int pad = S(28), full = ClientSize.Width - 2 * pad - SystemInformation.VerticalScrollBarWidth;
            y = S(20);
            DateTime last = w.End.AddDays(-1);
            Text2("Your week · " + w.Start.ToString("d MMM", En) + " – " + last.ToString("d MMM yyyy", En), Theme.FB(15), Theme.Text, pad, full);
            Text2(Headline(w), Theme.F(10), Theme.Muted, pad, full);
            y += S(10);

            // Tiles
            int tw = (full - 2 * S(12)) / 3;
            string delta = w.PrevMinutes >= 1 ? Delta(r.TotalMinutes, w.PrevMinutes) : "first tracked week";
            Tile(pad, tw, "Focused", Theme.Compact(r.TotalMinutes), delta);
            Tile(pad + tw + S(12), tw, "Pomodoros", r.Pomodoros.ToString(),
                r.Planned > 0 ? "of " + r.Planned + " planned · " + (int)Math.Round(100.0 * r.Pomodoros / r.Planned) + "%" : "completed");
            Tile(pad + 2 * (tw + S(12)), tw, "Streak", r.Streak + (r.Streak == 1 ? " day" : " days"), "in a row with a pomodoro");
            y += S(84);

            Section("By subject", pad, full);
            var breakdown = new SubjectBreakdown();
            breakdown.SetData(r);
            breakdown.SetBounds(pad, y, full, Math.Max(S(26), r.BySubject.Count * S(26)));
            body.Controls.Add(breakdown);
            y = breakdown.Bottom + S(6);

            Section("Your rhythm", pad, full);
            string rhythm = w.BestHour >= 0
                ? "You focused most between " + w.BestHour.ToString("00") + ":00 and " + (w.BestHour + 2).ToString("00") + ":00 — plan your hardest subject there."
                : "Not enough focus time yet to see your best hours.";
            if (w.BestDayMinutes > 0) rhythm += "\nBest day: " + w.BestDay.ToString("dddd", En) + " (" + Theme.Compact(w.BestDayMinutes) + ").";
            Text2(rhythm, Theme.F(10), Theme.Text, pad, full);

            Section("What you finished" + (w.Notes.Count > 0 ? "  (" + w.Notes.Count + ")" : ""), pad, full);
            if (w.Notes.Count == 0)
                Text2("No notes this week. After a pomodoro, write one line about what you got done — it adds up.", Theme.F(9.5f), Theme.Muted, pad, full);
            foreach (var n in w.Notes.Take(14))
            {
                var subj = Planner.Find(ctx.Settings, n.SubjectId);
                Text2(SessionStore.StartOf(n).ToString("ddd HH:mm", En) + (subj != null ? " · " + subj.Name : "") + "  —  " + n.Note, Theme.F(9.5f), Theme.Text, pad, full);
            }
            if (w.Notes.Count > 14) Text2("… and " + (w.Notes.Count - 14) + " more (Statistics → Notes).", Theme.F(9), Theme.Muted, pad, full);

            Section("Distractions you resisted" + (w.DistractionCount > 0 ? "  (" + w.DistractionCount + ")" : ""), pad, full);
            if (w.Distractions.Count == 0) Text2("Nothing was blocked this week — nice.",Theme.F(9.5f), Theme.Muted, pad, full);
            foreach (var d in w.Distractions)
                Text2(d.Name + (d.Kind == "app" ? " (app)" : "") + "  —  " + Insights.Describe(d), Theme.F(9.5f), Theme.Text, pad, full);

            int days;
            var next = Planner.NextDeadline(ctx.Settings, out days);
            if (next != null)
            {
                Section("Coming up", pad, full);
                Text2(next.Name + ": " + Planner.DeadlineText(next, days) + ".", Theme.F(10), days <= 7 ? Theme.ShortBreak : Theme.Text, pad, full);
            }
            var spacer = new Label { BackColor = Theme.Bg };
            spacer.SetBounds(0, y, 1, S(16));
            body.Controls.Add(spacer);
        }

        static string Headline(WeekReview w)
        {
            var r = w.Stats;
            if (r.TotalMinutes < 1) return "No focus time this week. A fresh start is one pomodoro away.";
            if (r.Planned > 0 && r.Pomodoros >= r.Planned) return "You did everything you planned. Great week!";
            if (w.PrevMinutes >= 1 && r.TotalMinutes > w.PrevMinutes * 1.1) return "More focus than last week. Keep the momentum!";
            return "Here's how your week went.";
        }

        static string Delta(double now, double before)
        {
            int pct = (int)Math.Round(100 * (now - before) / before);
            return (pct >= 0 ? "+" : "") + pct + "% vs last week";
        }

        void Tile(int x, int w, string label, string value, string sub)
        {
            var t = new StatTile();
            t.Set(label, value, sub);
            t.SetBounds(x, y, w, S(76));
            body.Controls.Add(t);
        }

        void Section(string text, int x, int w)
        {
            y += S(12);
            Text2(text.ToUpperInvariant(), Theme.FB(8.5f), Theme.Muted, x, w);
            y += S(2);
        }

        void Text2(string text, Font f, Color c, int x, int w)
        {
            int h = TextRenderer.MeasureText(text, f, new Size(w, 0), TextFormatFlags.WordBreak).Height + S(4);
            var l = new Label { Text = text, Font = f, ForeColor = c, BackColor = Theme.Bg, AutoSize = false, UseMnemonic = false };
            l.SetBounds(x, y, w, h);
            body.Controls.Add(l);
            y += h;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
            Native.DarkScrollbars(body.Handle);
        }
    }
}
