using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Fokkus
{
    // One focus phase (completed pomodoro, or the focused part of a skipped/stopped one).
    public class FocusRecord
    {
        public long StartMs { get; set; }      // Unix time, ms
        public int Seconds { get; set; }
        public string SubjectId { get; set; }
        public int Pomodoros { get; set; }     // 1 = completed, 0 = partial
    }

    public class SessionStore
    {
        readonly List<FocusRecord> records = new List<FocusRecord>();
        static string FilePath { get { return Path.Combine(AppSettings.Dir, "sessions.json"); } }

        public IList<FocusRecord> Records { get { return records; } }

        // Self-test mode: keep everything in memory.
        public bool ReadOnly;

        static JavaScriptSerializer Json()
        {
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js;
        }

        public static SessionStore Load()
        {
            var s = new SessionStore();
            try
            {
                if (File.Exists(FilePath))
                {
                    var list = Json().Deserialize<List<FocusRecord>>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (list != null) s.records.AddRange(list);
                }
                else s.MigrateOldStats();
            }
            catch (Exception ex) { Log.Error(ex); }
            return s;
        }

        // Older versions only kept per-day totals in stats.json.
        void MigrateOldStats()
        {
            string old = Path.Combine(AppSettings.Dir, "stats.json");
            if (!File.Exists(old)) return;
            var days = Json().Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(old, Encoding.UTF8));
            if (days == null) return;
            foreach (var kv in days)
            {
                DateTime d;
                if (!DateTime.TryParseExact(kv.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) continue;
                int pomos, minutes;
                kv.Value.TryGetValue("Pomodoros", out pomos);
                kv.Value.TryGetValue("FocusMinutes", out minutes);
                if (minutes <= 0) continue;
                records.Add(new FocusRecord { StartMs = ToUnixMs(d.AddHours(12)), Seconds = minutes * 60, Pomodoros = pomos });
            }
            Save();
        }

        public void Add(FocusRecord r)
        {
            records.Add(r);
            Save();
        }

        void Save()
        {
            if (ReadOnly) return;
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, Json().Serialize(records), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        public static long ToUnixMs(DateTime local)
        {
            return new DateTimeOffset(local).ToUnixTimeMilliseconds();
        }

        public static DateTime StartOf(FocusRecord r)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(r.StartMs).LocalDateTime;
        }

        public IEnumerable<FocusRecord> On(DateTime day)
        {
            DateTime d = day.Date;
            return records.Where(r => StartOf(r).Date == d);
        }

        public int PomodorosOn(DateTime day, string subjectId)
        {
            return On(day).Where(r => subjectId == null || r.SubjectId == subjectId).Sum(r => r.Pomodoros);
        }

        public int MinutesOn(DateTime day)
        {
            return (int)Math.Round(On(day).Sum(r => r.Seconds) / 60.0);
        }

        // Consecutive days with at least one completed pomodoro, ending today (or yesterday if today is still empty).
        public int Streak()
        {
            var days = new HashSet<DateTime>(records.Where(r => r.Pomodoros > 0).Select(r => StartOf(r).Date));
            DateTime d = DateTime.Today;
            if (!days.Contains(d)) d = d.AddDays(-1);
            int n = 0;
            while (days.Contains(d)) { n++; d = d.AddDays(-1); }
            return n;
        }
    }

    public enum StatRange { Day, Week, Month, Year }

    public class SeriesInfo
    {
        public string Id;      // subject id, or null for "Other / no subject"
        public string Name;
        public Color Color;
    }

    public class ChartBucket
    {
        public string Label;       // axis label ("" = skip)
        public string LongLabel;   // tooltip title
        public DateTime Start, End;
        public double[] Minutes;   // per series
        public double Total;
        public double Target = -1; // planned minutes, -1 = no plan
    }

    public class SubjectStat
    {
        public SeriesInfo Series;
        public double Minutes;
        public int Pomodoros;
        public int Planned;
    }

    public class StatsResult
    {
        public StatRange Range;
        public string Title;
        public DateTime Start, End;
        public List<SeriesInfo> Series = new List<SeriesInfo>();
        public List<ChartBucket> Buckets = new List<ChartBucket>();
        public List<SubjectStat> BySubject = new List<SubjectStat>();
        public double TotalMinutes;
        public int Pomodoros;
        public int Planned;
        public double DailyAverage;
        public string DailyAverageLabel;
        public int Streak;
    }

    static class Analytics
    {
        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-GB");

        public static DateTime WeekStart(DateTime d)
        {
            d = d.Date;
            return d.AddDays(-Planner.DayIndex(d));
        }

        public static StatsResult Compute(AppSettings settings, SessionStore store, StatRange range, DateTime anchor)
        {
            var res = new StatsResult();
            res.Range = range;
            anchor = anchor.Date;

            // Series: subjects with their own colour (in plan order), then one neutral "Other" bucket.
            var seriesIndex = new Dictionary<string, int>();
            foreach (var s in settings.Subjects.Where(s => s.ColorIndex >= 0 && s.ColorIndex < Theme.Series.Length))
            {
                seriesIndex[s.Id] = res.Series.Count;
                res.Series.Add(new SeriesInfo { Id = s.Id, Name = s.Name, Color = Theme.SubjectColor(s) });
            }
            int other = res.Series.Count;
            res.Series.Add(new SeriesInfo { Id = null, Name = settings.Subjects.Count > 0 ? "Other / no subject" : "Focus", Color = Theme.SeriesOther });
            int n = res.Series.Count;

            // Buckets
            switch (range)
            {
                case StatRange.Day:
                    res.Start = anchor; res.End = anchor.AddDays(1);
                    res.Title = anchor == DateTime.Today ? "Today" : anchor == DateTime.Today.AddDays(-1) ? "Yesterday" : anchor.ToString("ddd, d MMM yyyy", En);
                    for (int h = 0; h < 24; h++)
                        res.Buckets.Add(new ChartBucket
                        {
                            Label = h % 3 == 0 ? h.ToString("00") : "",
                            LongLabel = string.Format("{0:00}:00 – {1:00}:00", h, h + 1),
                            Start = anchor.AddHours(h), End = anchor.AddHours(h + 1),
                        });
                    break;
                case StatRange.Week:
                    res.Start = WeekStart(anchor); res.End = res.Start.AddDays(7);
                    {
                        DateTime last = res.End.AddDays(-1);
                        string span = res.Start.Month == last.Month
                            ? res.Start.Day + "–" + last.ToString("d MMM", En)
                            : res.Start.ToString("d MMM", En) + " – " + last.ToString("d MMM", En);
                        if (last.Year != DateTime.Today.Year) span += " " + last.Year;
                        res.Title = (res.Start == WeekStart(DateTime.Today) ? "This week · " : "") + span;
                    }
                    for (int i = 0; i < 7; i++)
                    {
                        var d = res.Start.AddDays(i);
                        res.Buckets.Add(new ChartBucket { Label = d.ToString("ddd", En), LongLabel = d.ToString("dddd, d MMM", En), Start = d, End = d.AddDays(1) });
                    }
                    break;
                case StatRange.Month:
                    res.Start = new DateTime(anchor.Year, anchor.Month, 1); res.End = res.Start.AddMonths(1);
                    res.Title = res.Start.ToString("MMMM yyyy", En);
                    for (var d = res.Start; d < res.End; d = d.AddDays(1))
                        res.Buckets.Add(new ChartBucket
                        {
                            Label = d.Day == 1 || d.Day % 5 == 0 ? d.Day.ToString() : "",
                            LongLabel = d.ToString("ddd, d MMM", En), Start = d, End = d.AddDays(1),
                        });
                    break;
                default:
                    res.Start = new DateTime(anchor.Year, 1, 1); res.End = res.Start.AddYears(1);
                    res.Title = anchor.Year.ToString();
                    for (int m = 0; m < 12; m++)
                    {
                        var d = res.Start.AddMonths(m);
                        res.Buckets.Add(new ChartBucket { Label = d.ToString("MMM", En), LongLabel = d.ToString("MMMM yyyy", En), Start = d, End = d.AddMonths(1) });
                    }
                    break;
            }
            foreach (var b in res.Buckets) b.Minutes = new double[n];

            // Distribute every record's focused minutes over the buckets it overlaps.
            var stats = new SubjectStat[n];
            for (int i = 0; i < n; i++) stats[i] = new SubjectStat { Series = res.Series[i] };
            foreach (var r in store.Records)
            {
                DateTime s0 = SessionStore.StartOf(r), s1 = s0.AddSeconds(r.Seconds);
                if (s1 <= res.Start || s0 >= res.End) continue;
                int si;
                if (r.SubjectId == null || !seriesIndex.TryGetValue(r.SubjectId, out si)) si = other;
                double inRange = 0;
                foreach (var b in res.Buckets)
                {
                    DateTime a = s0 > b.Start ? s0 : b.Start, z = s1 < b.End ? s1 : b.End;
                    if (z <= a) continue;
                    double min = (z - a).TotalMinutes;
                    b.Minutes[si] += min;
                    inRange += min;
                }
                stats[si].Minutes += inRange;
                if (s0 >= res.Start && s0 < res.End) { stats[si].Pomodoros += r.Pomodoros; res.Pomodoros += r.Pomodoros; }
            }
            foreach (var b in res.Buckets) b.Total = b.Minutes.Sum();
            res.TotalMinutes = res.Buckets.Sum(b => b.Total);

            // Plan targets (planned pomodoros x focus length) per day, week view and up - only from the day the plan exists.
            DateTime planStart = settings.PlanStart;
            bool hasPlan = settings.Subjects.Any(s => s.Targets.Sum() > 0);
            if (hasPlan && range != StatRange.Day)
                foreach (var b in res.Buckets)
                {
                    if (b.End <= planStart) continue;
                    int planned = 0;
                    for (var d = b.Start; d < b.End; d = d.AddDays(1))
                        if (d >= planStart) planned += settings.Subjects.Sum(s => Planner.Target(s, d));
                    b.Target = planned * settings.FocusMinutes;
                }

            // Planned pomodoros per subject for the elapsed part of the range.
            DateTime planEnd = res.End < DateTime.Today.AddDays(1) ? res.End : DateTime.Today.AddDays(1);
            DateTime planFrom = res.Start > planStart ? res.Start : planStart;
            foreach (var s in settings.Subjects)
            {
                int planned = 0;
                for (var d = planFrom; d < planEnd; d = d.AddDays(1)) planned += Planner.Target(s, d);
                int si;
                if (seriesIndex.TryGetValue(s.Id, out si)) stats[si].Planned += planned;
                else stats[other].Planned += planned;
                res.Planned += planned;
            }
            res.BySubject = stats.Where(x => x.Minutes > 0.5 || x.Planned > 0 || x.Pomodoros > 0).OrderByDescending(x => x.Minutes).ToList();

            // Daily average over the days of the range that have already started.
            if (range == StatRange.Day)
            {
                double last30 = store.Records.Where(r => SessionStore.StartOf(r) >= DateTime.Today.AddDays(-29)).Sum(r => r.Seconds) / 60.0;
                res.DailyAverage = last30 / 30.0;
                res.DailyAverageLabel = "30-day daily avg";
            }
            else
            {
                // Average over the days you've been using Fokkus within this range.
                DateTime until = res.End < DateTime.Today.AddDays(1) ? res.End : DateTime.Today.AddDays(1);
                DateTime from = res.Start;
                if (store.Records.Count > 0)
                {
                    DateTime first = store.Records.Min(r => SessionStore.StartOf(r)).Date;
                    if (first > from) from = first;
                }
                int days = Math.Max(1, (int)Math.Ceiling((until - from).TotalDays));
                res.DailyAverage = res.TotalMinutes / days;
                res.DailyAverageLabel = "Daily average";
            }
            res.Streak = store.Streak();
            return res;
        }

        public static DateTime Shift(StatRange range, DateTime anchor, int dir)
        {
            switch (range)
            {
                case StatRange.Day: return anchor.AddDays(dir);
                case StatRange.Week: return anchor.AddDays(7 * dir);
                case StatRange.Month: return anchor.AddMonths(dir);
                default: return anchor.AddYears(dir);
            }
        }
    }
}
