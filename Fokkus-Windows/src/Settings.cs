using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Fokkus
{
    // A subject in the weekly study plan. Targets = pomodoros per weekday, Monday first.
    public class Subject
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int ColorIndex { get; set; }
        public List<int> Targets { get; set; }

        public Subject()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            Name = "Subject";
            Targets = new List<int> { 0, 0, 0, 0, 0, 0, 0 };
        }
    }

    public class AppSettings
    {
        // Study plan
        public List<Subject> Subjects { get; set; }
        public string ActiveSubjectId { get; set; }
        public bool AutoNextSubject { get; set; }
        public string PlanSince { get; set; }   // yyyy-MM-dd: plan targets count from this day on

        public DateTime PlanStart
        {
            get
            {
                DateTime d;
                return PlanSince != null && DateTime.TryParseExact(PlanSince, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                    ? d : DateTime.MaxValue;
            }
        }

        // Timer
        public int FocusMinutes { get; set; }
        public int ShortBreakMinutes { get; set; }
        public int LongBreakMinutes { get; set; }
        public int SessionsBeforeLongBreak { get; set; }
        public bool AutoStartBreaks { get; set; }
        public bool AutoStartFocus { get; set; }
        public bool StrictMode { get; set; }
        public bool SoundEnabled { get; set; }

        // Websites
        public bool BlockWebsites { get; set; }
        public bool AllowListMode { get; set; }
        public List<string> AllowedSites { get; set; }
        public List<string> BlockedSites { get; set; }

        // Apps
        public bool BlockApps { get; set; }
        public List<string> BlockedApps { get; set; }

        // Tips
        public bool TipsEnabled { get; set; }
        public int TipIntervalMinutes { get; set; }
        public bool BreakSuggestions { get; set; }
        public List<string> DisabledTipCategories { get; set; }

        // Window / general
        public int MiniTimerMode { get; set; }   // 0 = always, 1 = during sessions, 2 = off
        public int MiniOpacity { get; set; }
        public int MiniX { get; set; }
        public int MiniY { get; set; }
        public bool StartWithWindows { get; set; }
        public bool CloseToTray { get; set; }

        public AppSettings()
        {
            Subjects = new List<Subject>();
            AutoNextSubject = true;
            FocusMinutes = 25;
            ShortBreakMinutes = 5;
            LongBreakMinutes = 15;
            SessionsBeforeLongBreak = 4;
            AutoStartBreaks = true;
            AutoStartFocus = false;
            StrictMode = false;
            SoundEnabled = true;

            BlockWebsites = true;
            AllowListMode = true;
            AllowedSites = Presets.Collect(Presets.AllowSites, Presets.DefaultAllowPresets);
            BlockedSites = Presets.Collect(Presets.BlockSites, Presets.DefaultBlockPresets);

            BlockApps = true;
            BlockedApps = Presets.Collect(Presets.Apps, Presets.DefaultAppPresets);

            TipsEnabled = true;
            TipIntervalMinutes = 20;
            BreakSuggestions = true;
            DisabledTipCategories = new List<string>();

            MiniTimerMode = 0;
            MiniOpacity = 94;
            MiniX = -1;
            MiniY = -1;
            StartWithWindows = false;
            CloseToTray = true;
        }

        static bool migrated;

        public static string Dir
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string d = Path.Combine(appData, "Fokkus");
                if (!migrated)
                {
                    migrated = true;
                    // The app used to be called kk-Lock-in: carry its data over once.
                    string old = Path.Combine(appData, "kk-Lock-in");
                    if (!Directory.Exists(d) && Directory.Exists(old))
                    {
                        Directory.CreateDirectory(d);
                        foreach (var f in Directory.GetFiles(old))
                            try { File.Copy(f, Path.Combine(d, Path.GetFileName(f)), false); } catch { }
                    }
                }
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var s = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(FilePath, Encoding.UTF8));
                    if (s != null) { s.Normalize(); return s; }
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Normalize();
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(this), Encoding.UTF8);
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }

        static List<string> Clean(List<string> list, Func<string, string> norm)
        {
            if (list == null) return new List<string>();
            return list.Select(norm).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.OrdinalIgnoreCase)
                       .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        void Normalize()
        {
            FocusMinutes = Clamp(FocusMinutes, 1, 240);
            ShortBreakMinutes = Clamp(ShortBreakMinutes, 1, 60);
            LongBreakMinutes = Clamp(LongBreakMinutes, 1, 120);
            SessionsBeforeLongBreak = Clamp(SessionsBeforeLongBreak, 1, 12);
            TipIntervalMinutes = Clamp(TipIntervalMinutes, 5, 120);
            MiniTimerMode = Clamp(MiniTimerMode, 0, 2);
            MiniOpacity = Clamp(MiniOpacity, 30, 100);
            AllowedSites = Clean(AllowedSites, HostRules.Normalize);
            BlockedSites = Clean(BlockedSites, HostRules.Normalize);
            BlockedApps = Clean(BlockedApps, AppBlocker.CleanName);
            if (DisabledTipCategories == null) DisabledTipCategories = new List<string>();
            if (Subjects == null) Subjects = new List<Subject>();
            foreach (var s in Subjects)
            {
                if (string.IsNullOrEmpty(s.Id)) s.Id = Guid.NewGuid().ToString("N").Substring(0, 10);
                if (string.IsNullOrWhiteSpace(s.Name)) s.Name = "Subject";
                if (s.Targets == null) s.Targets = new List<int>();
                while (s.Targets.Count < 7) s.Targets.Add(0);
                if (s.Targets.Count > 7) s.Targets = s.Targets.Take(7).ToList();
                for (int i = 0; i < 7; i++) s.Targets[i] = Clamp(s.Targets[i], 0, 16);
            }
            if (ActiveSubjectId != null && !Subjects.Any(x => x.Id == ActiveSubjectId)) ActiveSubjectId = null;
            if (Subjects.Count > 0 && PlanSince == null) PlanSince = DateTime.Today.ToString("yyyy-MM-dd");
        }

        // Next free colour slot (0..7); 8 = the neutral "other" colour once all are taken.
        public int NextColorIndex()
        {
            for (int i = 0; i < Theme.Series.Length; i++)
                if (!Subjects.Any(s => s.ColorIndex == i)) return i;
            return Theme.Series.Length;
        }
    }

    static class Log
    {
        public static void Error(Exception ex)
        {
            if (ex == null) return;
            Write("ERROR " + ex);
        }

        public static void Write(string line)
        {
            try
            {
                string path = Path.Combine(AppSettings.Dir, "log.txt");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch { }
        }
    }
}
