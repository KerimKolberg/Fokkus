using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Fokkus
{
    // One blocked attempt: a distracting website or a blocked / minimized app.
    public class DistractionEntry
    {
        public long T { get; set; }     // Unix time, ms
        public string K { get; set; }   // "site" or "app"
        public string N { get; set; }   // domain or app name
    }

    // Remembers which distractions you tried to open during focus (only real distractions, not background requests).
    public class DistractionLog
    {
        const int MaxEntries = 20000;
        readonly List<DistractionEntry> entries = new List<DistractionEntry>();
        readonly Dictionary<string, DateTime> lastSeen = new Dictionary<string, DateTime>();
        bool dirty;
        public bool ReadOnly;

        static string FilePath { get { return Path.Combine(AppSettings.Dir, "distractions.json"); } }

        public IList<DistractionEntry> Entries { get { return entries; } }

        static JavaScriptSerializer Json()
        {
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js;
        }

        public static DistractionLog Load()
        {
            var log = new DistractionLog();
            try
            {
                if (File.Exists(FilePath))
                {
                    var list = Json().Deserialize<List<DistractionEntry>>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (list != null) log.entries.AddRange(list.Where(e => e != null && !string.IsNullOrEmpty(e.N)));
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            return log;
        }

        // One attempt per thing per minute: a single page load fires many requests.
        public bool Add(string kind, string name, DateTime now)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string key = kind + ":" + name.ToLowerInvariant();
            DateTime last;
            if (lastSeen.TryGetValue(key, out last) && (now - last).TotalSeconds < 60) return false;
            lastSeen[key] = now;
            entries.Add(new DistractionEntry { T = SessionStore.ToUnixMs(now), K = kind, N = name });
            if (entries.Count > MaxEntries) entries.RemoveRange(0, entries.Count - MaxEntries);
            dirty = true;
            return true;
        }

        public void Merge(IEnumerable<DistractionEntry> incoming)
        {
            var have = new HashSet<string>(entries.Select(e => e.T + e.K + e.N));
            foreach (var e in incoming)
                if (e != null && !string.IsNullOrEmpty(e.N) && have.Add(e.T + e.K + e.N)) { entries.Add(e); dirty = true; }
            entries.Sort((a, b) => a.T.CompareTo(b.T));
            SaveIfDirty();
        }

        public void SaveIfDirty()
        {
            if (!dirty || ReadOnly) return;
            dirty = false;
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json().Serialize(entries), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error(ex); }
        }
    }

    public class DistractionStat
    {
        public string Kind, Name;
        public int Count;
        public int PeakHour;
    }

    static class Insights
    {
        public static DateTime Local(long ms) { return DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime; }

        public static int Count(DistractionLog log, DateTime start, DateTime end)
        {
            long a = SessionStore.ToUnixMs(start), b = SessionStore.ToUnixMs(end);
            return log.Entries.Count(e => e.T >= a && e.T < b);
        }

        // Most tried distractions in a period, with the hour they usually happen.
        public static List<DistractionStat> Top(DistractionLog log, DateTime start, DateTime end, int max)
        {
            long a = SessionStore.ToUnixMs(start), b = SessionStore.ToUnixMs(end);
            return log.Entries.Where(e => e.T >= a && e.T < b)
                .GroupBy(e => e.K + "\n" + e.N.ToLowerInvariant())
                .Select(g => new DistractionStat
                {
                    Kind = g.First().K,
                    Name = g.First().N,
                    Count = g.Count(),
                    PeakHour = g.GroupBy(e => Local(e.T).Hour).OrderByDescending(h => h.Count()).ThenBy(h => h.Key).First().Key,
                })
                .OrderByDescending(d => d.Count).ThenBy(d => d.Name)
                .Take(max).ToList();
        }

        public static string Describe(DistractionStat d)
        {
            return d.Count + "× · mostly around " + d.PeakHour.ToString("00") + ":00";
        }
    }

    // Smarter breaks: a longer break after a long block, or after skipping breaks.
    static class Breaks
    {
        // Suggested break length in minutes, or 0 to keep the planned one.
        public static int Suggest(int focusMinutes, int breakMinutes, int skippedInRow)
        {
            int s = breakMinutes;
            if (focusMinutes >= 50) s = Math.Max(s, (int)(Math.Ceiling(focusMinutes / 5.0 / 5.0) * 5));
            if (skippedInRow >= 2) s = Math.Max(s, breakMinutes + 5);
            return s > breakMinutes ? s : 0;
        }

        public static string Reason(int focusMinutes, int skippedInRow)
        {
            if (focusMinutes >= 50) return "That was a long block (" + focusMinutes + " min).";
            return "You skipped your last " + skippedInRow + " breaks.";
        }
    }

    public class WeekReview
    {
        public DateTime Start, End;
        public StatsResult Stats;
        public double PrevMinutes;
        public int BestHour = -1;          // start of the best 2-hour window, -1 = not enough data
        public DateTime BestDay;
        public double BestDayMinutes;
        public List<FocusRecord> Notes = new List<FocusRecord>();
        public List<DistractionStat> Distractions = new List<DistractionStat>();
        public int DistractionCount;
    }

    static class Review
    {
        public static WeekReview Compute(AppSettings s, SessionStore store, DistractionLog log, DateTime anyDayOfWeek)
        {
            var w = new WeekReview();
            w.Stats = Analytics.Compute(s, store, StatRange.Week, anyDayOfWeek);
            w.Start = w.Stats.Start;
            w.End = w.Stats.End;
            w.PrevMinutes = Analytics.Compute(s, store, StatRange.Week, w.Start.AddDays(-7)).TotalMinutes;

            // When do you focus? Minutes per hour of the day, then the best 2-hour window.
            var hours = new double[24];
            foreach (var r in store.Records)
            {
                DateTime s0 = SessionStore.StartOf(r), s1 = s0.AddSeconds(r.Seconds);
                if (s1 <= w.Start || s0 >= w.End) continue;
                for (DateTime h = new DateTime(s0.Year, s0.Month, s0.Day, s0.Hour, 0, 0); h < s1; h = h.AddHours(1))
                {
                    DateTime a = s0 > h ? s0 : h, z = s1 < h.AddHours(1) ? s1 : h.AddHours(1);
                    if (z > a && a >= w.Start && a < w.End) hours[h.Hour] += (z - a).TotalMinutes;
                }
            }
            if (hours.Sum() >= 30)
            {
                double best = -1;
                for (int h = 0; h < 23; h++)
                    if (hours[h] + hours[h + 1] > best) { best = hours[h] + hours[h + 1]; w.BestHour = h; }
            }
            var day = w.Stats.Buckets.OrderByDescending(b => b.Total).FirstOrDefault();
            if (day != null && day.Total > 0) { w.BestDay = day.Start; w.BestDayMinutes = day.Total; }

            w.Notes = store.Records.Where(r => !string.IsNullOrEmpty(r.Note))
                .Where(r => { var t = SessionStore.StartOf(r); return t >= w.Start && t < w.End; })
                .OrderBy(r => r.StartMs).ToList();
            w.Distractions = Insights.Top(log, w.Start, w.End, 4);
            w.DistractionCount = Insights.Count(log, w.Start, w.End);
            return w;
        }

        // The week to offer: on Sunday evening this week, on any other day the week before.
        public static DateTime DueWeek(DateTime now)
        {
            DateTime week = Analytics.WeekStart(now);
            return now.DayOfWeek == DayOfWeek.Sunday && now.Hour >= 18 ? week : week.AddDays(-7);
        }
    }
}
