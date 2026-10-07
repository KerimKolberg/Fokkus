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
                var row = new PlanRow { Subject = subj, Target = Target(subj, today), Done = store.PomodorosOn(today, subj.Id) };
                if (row.Target > 0 || row.Done > 0) rows.Add(row);
            }
            return rows;
        }

        // First subject that still has pomodoros left today (keeps the preferred one if it does).
        public static Subject NextPending(AppSettings s, SessionStore store, string preferId)
        {
            var rows = Today(s, store).Where(r => r.Done < r.Target).ToList();
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
