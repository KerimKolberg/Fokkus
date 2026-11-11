using System;
using System.Collections.Generic;
using System.Linq;

namespace Fokkus
{
    public class PlanRow
    {
        public Subject Subject;
        public int Target;
        public int Done;
        public int? DaysLeft;   // days until the subject's deadline (0 = today), null if none
        public int Extra;       // extra pomodoros suggested today because the deadline is close
    }

    // The weekly study plan: which subject today, and how far along you are.
    static class Planner
    {
        public static readonly string[] DayShort = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        public static int DayIndex(DateTime d) { return ((int)d.DayOfWeek + 6) % 7; }

        public static int Target(Subject s, DateTime d)
        {
            int i = DayIndex(d);
            return s.Targets != null && i < s.Targets.Count ? s.Targets[i] : 0;
        }

        public static Subject Find(AppSettings s, string id)
        {
            return id == null ? null : s.Subjects.FirstOrDefault(x => x.Id == id);
        }

        public static Subject Active(AppSettings s) { return Find(s, s.ActiveSubjectId); }

        public static List<PlanRow> Today(AppSettings s, SessionStore store)
        {
            var today = DateTime.Today;
            var rows = new List<PlanRow>();
            foreach (var subj in s.Subjects)
            {
                var row = new PlanRow { Subject = subj, Target = Target(subj, today), Done = store.PomodorosOn(today, subj.Id), DaysLeft = DaysLeft(subj, today) };
                row.Extra = row.DaysLeft.HasValue ? ExtraFor(row.DaysLeft.Value) : 0;
                if (row.Target > 0 || row.Done > 0 || row.Extra > 0) rows.Add(row);
            }
            return rows;
        }

        // ---- deadlines

        public static int? DaysLeft(Subject s, DateTime today)
        {
            var d = s.DeadlineDate;
            if (!d.HasValue) return null;
            int days = (int)(d.Value.Date - today.Date).TotalDays;
            return days >= 0 ? days : (int?)null;
        }

        // Close to a deadline you get a nudge for more: +1 pomodoro in the last week, +2 in the last 3 days.
        public static int ExtraFor(int daysLeft) { return daysLeft <= 3 ? 2 : daysLeft <= 7 ? 1 : 0; }

        public static string DeadlineText(Subject s, int daysLeft)
        {
            string label = string.IsNullOrWhiteSpace(s.DeadlineLabel) ? "Deadline" : s.DeadlineLabel.Trim();
            return label + (daysLeft == 0 ? " today" : daysLeft == 1 ? " tomorrow" : " in " + daysLeft + " days");
        }

        public static string ShortDeadline(int daysLeft) { return daysLeft == 0 ? "today" : daysLeft == 1 ? "1 day" : daysLeft + " days"; }

        // Small badge for today's plan, e.g. "Exam 5d · +1" (shown in the last two weeks).
        public static string Badge(PlanRow r)
        {
            if (!r.DaysLeft.HasValue || r.DaysLeft.Value > 14) return null;
            string label = string.IsNullOrWhiteSpace(r.Subject.DeadlineLabel) ? "Due" : r.Subject.DeadlineLabel.Trim();
            if (label.Length > 10) label = label.Substring(0, 10);
            int d = r.DaysLeft.Value;
            return label + " " + (d == 0 ? "today" : d + "d") + (r.Extra > 0 ? " · +" + r.Extra : "");
        }

        // The nearest upcoming deadline across all subjects, or null.
        public static Subject NextDeadline(AppSettings s, out int daysLeft)
        {
            Subject best = null;
            daysLeft = int.MaxValue;
            foreach (var subj in s.Subjects)
            {
                var d = DaysLeft(subj, DateTime.Today);
                if (d.HasValue && d.Value < daysLeft) { best = subj; daysLeft = d.Value; }
            }
            return best;
        }

        // First subject that still has pomodoros left today (keeps the preferred one if it does).
        // Subjects with the nearest deadline come first, then the plan order.
        public static Subject NextPending(AppSettings s, SessionStore store, string preferId)
        {
            var rows = Today(s, store).Where(r => r.Done < r.Target)
                                      .OrderBy(r => r.DaysLeft.HasValue ? r.DaysLeft.Value : int.MaxValue).ToList();
            var keep = rows.FirstOrDefault(r => r.Subject.Id == preferId);
            if (keep != null) return keep.Subject;
            return rows.Count > 0 ? rows[0].Subject : null;
        }

        public static void TodayTotals(AppSettings s, SessionStore store, out int done, out int target)
        {
            var rows = Today(s, store);
            target = rows.Sum(r => r.Target);
            done = rows.Sum(r => Math.Min(r.Done, r.Target));
        }
    }

    static class Motivation
    {
        static readonly string[] focus =
        {
            "One pomodoro at a time.", "Deep work, deep results.", "Stay locked in.", "Small steps, big results.",
            "Discipline beats motivation.", "Future you says thanks.", "Progress, not perfection.", "You've got this.",
            "Focus is a superpower.", "Brick by brick.", "Hard now, easy later.", "Make this session count.",
            "Consistency wins.", "Your goals are waiting.", "Just this one task.", "Breathe. Focus. Go.",
            "Done is better than perfect.", "Every pomodoro counts.",
        };

        static readonly string[] rest =
        {
            "Rest is part of the work.", "Recharge, then conquer.", "Breathe. Stretch. Smile.",
            "Drink some water.", "Look away from the screen.", "Move a little, feel better.",
        };

        static readonly string[] idle =
        {
            "Ready when you are.", "Start small. Start now.", "The hardest part is starting.",
            "Let's make today count.", "One session changes the day.",
        };

        static readonly int seed = new Random().Next(1000);

        // Changes once a minute; reacts to the moment (last minutes, plan done...).
        public static string Current(PomodoroEngine eng, int planDone, int planTarget)
        {
            if (eng.IsLockedIn && eng.State == RunState.Running && eng.Remaining.TotalSeconds < 180)
                return "Final stretch — finish strong!";
            if (planTarget > 0 && planDone >= planTarget) return "Today's plan is done. Legend!";
            string[] list = eng.State == RunState.Idle ? idle : eng.Phase == Phase.Focus ? focus : rest;
            long slot = DateTime.Now.Ticks / (TimeSpan.TicksPerSecond * 60);
            return list[(int)((slot + seed) % list.Length)];
        }
    }
}
