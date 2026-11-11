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
        public string Deadline { get; set; }        // yyyy-MM-dd, or null
        public string DeadlineLabel { get; set; }   // "Exam", "Project due"...
        public string ProfileId { get; set; }       // profile switched on when you study this subject, or null

        public Subject()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            Name = "Subject";
            Targets = new List<int> { 0, 0, 0, 0, 0, 0, 0 };
        }

        [ScriptIgnore]
        public DateTime? DeadlineDate
        {
            get
            {
                DateTime d;
                return Deadline != null && DateTime.TryParseExact(Deadline, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                    ? d : (DateTime?)null;
            }
        }
    }

    // A saved combination of the four blocking functions + strict mode ("Programming", "Exam prep"...).
    public class Profile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool AllowSitesOn { get; set; }
        public bool BlockSitesOn { get; set; }
        public bool AllowAppsOn { get; set; }
        public bool BlockAppsOn { get; set; }
        public bool Strict { get; set; }

        public Profile() { Id = Guid.NewGuid().ToString("N").Substring(0, 10); Name = "Profile"; }

        static Profile Make(string id, string name, bool allowSites, bool blockSites, bool allowApps, bool blockApps, bool strict)
        {
            return new Profile { Id = id, Name = name, AllowSitesOn = allowSites, BlockSitesOn = blockSites, AllowAppsOn = allowApps, BlockAppsOn = blockApps, Strict = strict };
        }

        public static List<Profile> Defaults()
        {
            return new List<Profile>
            {
                Make("study", "Study", true, false, false, true, false),
                Make("coding", "Programming", false, true, false, true, false),
                Make("exam", "Exam prep", true, true, true, true, true),
                Make("timer", "Just the timer", false, false, false, false, false),
            };
        }

        public bool Matches(AppSettings s)
        {
            return s.AllowSitesOn == AllowSitesOn && s.BlockSitesOn == BlockSitesOn && s.AllowAppsOn == AllowAppsOn &&
                   s.BlockAppsOn == BlockAppsOn && s.StrictMode == Strict;
        }

        public void CaptureFrom(AppSettings s)
        {
            AllowSitesOn = s.AllowSitesOn; BlockSitesOn = s.BlockSitesOn; AllowAppsOn = s.AllowAppsOn; BlockAppsOn = s.BlockAppsOn; Strict = s.StrictMode;
        }

        // Switching from the current settings to this profile would loosen the lock.
        public bool Loosens(AppSettings s)
        {
            return (s.AllowSitesOn && !AllowSitesOn) || (s.BlockSitesOn && !BlockSitesOn) || (s.AllowAppsOn && !AllowAppsOn) ||
                   (s.BlockAppsOn && !BlockAppsOn) || (s.StrictMode && !Strict);
        }

        public void ApplyTo(AppSettings s)
        {
            s.AllowSitesOn = AllowSitesOn; s.BlockSitesOn = BlockSitesOn; s.AllowAppsOn = AllowAppsOn; s.BlockAppsOn = BlockAppsOn; s.StrictMode = Strict;
            s.ActiveProfileId = Id;
        }
    }

    // The four blocking functions, each switched on or off on its own.
    public enum Filter { AllowSites, BlockSites, AllowApps, BlockApps }

    public class AppSettings
    {
        // Study plan
        public List<Subject> Subjects { get; set; }
        public string ActiveSubjectId { get; set; }
        public bool AutoNextSubject { get; set; }
        public string PlanSince { get; set; }   // yyyy-MM-dd: plan targets count from this day on

        // Session profiles
        public List<Profile> Profiles { get; set; }
        public string ActiveProfileId { get; set; }   // the profile applied last (it may have been changed since)

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

        // Websites: two independent functions.
        //   AllowSitesOn -> during focus only the allowed sites work
        //   BlockSitesOn -> during focus the blocked sites never work (also wins over the allowed list)
        public bool AllowSitesOn { get; set; }
        public bool BlockSitesOn { get; set; }
        public List<string> AllowedSites { get; set; }
        public List<string> BlockedSites { get; set; }

        // Apps: the same two functions.
        //   AllowAppsOn -> any other app window is minimized during focus (never closed, so no work is lost)
        //   BlockAppsOn -> blocked apps are closed during focus
        public bool AllowAppsOn { get; set; }
        public bool BlockAppsOn { get; set; }
        public List<string> AllowedApps { get; set; }
        public List<string> BlockedApps { get; set; }

        // Older versions (read only to upgrade saved settings).
        public bool BlockWebsites { get; set; }
        public bool AllowListMode { get; set; }
        public bool BlockApps { get; set; }
        public List<string> DisabledAllowedSites { get; set; }
        public List<string> DisabledBlockedSites { get; set; }
        public List<string> DisabledApps { get; set; }

        // Bumped when defaults change, so existing settings get the new defaults once.
        public const int CurrentVersion = 5;
        public int SettingsVersion { get; set; }

        public bool IsOn(Filter f)
        {
            switch (f)
            {
                case Filter.AllowSites: return AllowSitesOn;
                case Filter.BlockSites: return BlockSitesOn;
                case Filter.AllowApps: return AllowAppsOn;
                default: return BlockAppsOn;
            }
        }

        public void SetOn(Filter f, bool on)
        {
            switch (f)
            {
                case Filter.AllowSites: AllowSitesOn = on; break;
                case Filter.BlockSites: BlockSitesOn = on; break;
                case Filter.AllowApps: AllowAppsOn = on; break;
                default: BlockAppsOn = on; break;
            }
        }

        public static bool Has(List<string> list, string item)
        {
            return list.Any(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
        }

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
        public bool MinimizeToTray { get; set; }   // minimize hides the window in the tray instead of the taskbar

        // Notes, smarter breaks, global shortcuts
        public bool AskForNotes { get; set; }      // "what did you finish?" after each pomodoro
        public bool SmartBreaks { get; set; }      // longer break after a long block or skipped breaks
        public bool HotkeysEnabled { get; set; }   // Ctrl+Alt+F / S / T / O

        // Focus music (plays only while a focus phase runs)
        public bool MusicOn { get; set; }
        public string MusicStation { get; set; }
        public int MusicVolume { get; set; }
        public List<CustomStation> CustomStations { get; set; }

        // Weekly review: Monday (yyyy-MM-dd) of the last week that was offered
        public string LastReviewedWeek { get; set; }

        // Sync with the phone through a shared cloud folder
        public string SyncFolder { get; set; }
        public string SyncDeviceId { get; set; }
        public long LastSyncMs { get; set; }
        public long PlanUpdatedMs { get; set; }    // when the study plan was last changed (newest plan wins)
        public string PlanSignature { get; set; }

        public AppSettings()
        {
            Subjects = new List<Subject>();
            Profiles = Profile.Defaults();
            AutoNextSubject = true;
            FocusMinutes = 25;
            ShortBreakMinutes = 5;
            LongBreakMinutes = 15;
            SessionsBeforeLongBreak = 4;
            AutoStartBreaks = true;
            AutoStartFocus = false;
            StrictMode = false;
            SoundEnabled = true;

            AllowSitesOn = true;
            BlockSitesOn = false;
            AllowedSites = Presets.Collect(Presets.AllowSites, Presets.DefaultAllowPresets);
            BlockedSites = Presets.Collect(Presets.BlockSites, Presets.DefaultBlockPresets);

            AllowAppsOn = false;
            BlockAppsOn = true;
            AllowedApps = Presets.Collect(Presets.AllowApps, Presets.DefaultAllowAppPresets);
            BlockedApps = Presets.Collect(Presets.Apps, Presets.DefaultAppPresets);

            BlockWebsites = true;
            AllowListMode = true;
            BlockApps = true;
            DisabledAllowedSites = new List<string>();
            DisabledBlockedSites = new List<string>();
            DisabledApps = new List<string>();

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
            MinimizeToTray = true;

            AskForNotes = true;
            SmartBreaks = true;
            HotkeysEnabled = true;
            MusicOn = false;
            MusicStation = "lofi";
            MusicVolume = 40;
            CustomStations = new List<CustomStation>();
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
                    if (s != null)
                    {
                        s.Normalize();
                        if (s.SettingsVersion < CurrentVersion) { s.Upgrade(); s.Save(); }
                        return s;
                    }
                }
            }
            catch (Exception ex) { Log.Error(ex); }
            var fresh = new AppSettings();
            fresh.SettingsVersion = CurrentVersion;
            return fresh;
        }

        // One-time changes for settings saved by older versions.
        internal void Upgrade()
        {
            if (SettingsVersion < 2)
            {
                // v2: GitHub & coding sites (GitHub sign-in, VS Code, package registries) are allowed by default.
                foreach (var p in Presets.AllowSites.Where(x => x.Name == Presets.CodingPreset))
                    foreach (var item in p.Items)
                        if (!Has(AllowedSites, item)) AllowedSites.Add(item);
            }
            if (SettingsVersion < 3)
            {
                // v3: separate on/off functions (allowed / blocked) instead of one mode switch,
                // and no more per-entry switches: entries that were switched off are removed.
                AllowSitesOn = BlockWebsites && AllowListMode;
                BlockSitesOn = BlockWebsites && !AllowListMode;
                BlockAppsOn = BlockApps;
                AllowAppsOn = false;
                AllowedSites.RemoveAll(x => Has(DisabledAllowedSites, x));
                BlockedSites.RemoveAll(x => Has(DisabledBlockedSites, x));
                BlockedApps.RemoveAll(x => Has(DisabledApps, x));
                DisabledAllowedSites.Clear();
                DisabledBlockedSites.Clear();
                DisabledApps.Clear();
            }
            if (SettingsVersion < 4)
            {
                // v4: YouTube is for study music - off the blocked list, onto the allowed list.
                BlockedSites.RemoveAll(x => Presets.YouTubeHosts.Any(y => x == y || x.EndsWith("." + y, StringComparison.Ordinal)));
                foreach (var p in Presets.AllowSites.Where(x => x.Name == Presets.YouTubePreset))
                    foreach (var item in p.Items)
                        if (!Has(AllowedSites, item)) AllowedSites.Add(item);
            }
            // v5: minimizing has its own setting; it used to follow "Closing the window keeps it in the tray".
            if (SettingsVersion < 5) MinimizeToTray = CloseToTray;
            SettingsVersion = CurrentVersion;
        }

        // Self-test / screenshot mode: never write the real settings file.
        public static bool ReadOnly;

        // Fingerprint of everything that is synced as "the plan" (subjects, targets, deadlines, links).
        public string ComputePlanSignature()
        {
            var sb = new StringBuilder();
            sb.Append(PlanSince).Append('|').Append(AutoNextSubject);
            foreach (var s in Subjects)
                sb.Append('|').Append(s.Id).Append(',').Append(s.Name).Append(',').Append(s.ColorIndex).Append(',')
                  .Append(string.Join(".", s.Targets)).Append(',').Append(s.Deadline).Append(',').Append(s.DeadlineLabel).Append(',').Append(s.ProfileId);
            return sb.ToString();
        }

        public void Save()
        {
            Normalize();
            // Any change to the plan gets a timestamp, so the newest plan wins when syncing.
            string sig = ComputePlanSignature();
            if (sig != PlanSignature)
            {
                PlanSignature = sig;
                PlanUpdatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
            if (ReadOnly) return;
            try
            {
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
            AllowedApps = Clean(AllowedApps, AppBlocker.CleanName);
            if (DisabledAllowedSites == null) DisabledAllowedSites = new List<string>();
            if (DisabledBlockedSites == null) DisabledBlockedSites = new List<string>();
            if (DisabledApps == null) DisabledApps = new List<string>();
            if (DisabledTipCategories == null) DisabledTipCategories = new List<string>();
            MusicVolume = Clamp(MusicVolume, 0, 100);
            if (string.IsNullOrEmpty(MusicStation)) MusicStation = "lofi";
            if (CustomStations == null) CustomStations = new List<CustomStation>();
            CustomStations.RemoveAll(c => c == null || string.IsNullOrWhiteSpace(c.Url));
            if (string.IsNullOrEmpty(SyncDeviceId)) SyncDeviceId = "win-" + Guid.NewGuid().ToString("N").Substring(0, 8);
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
            if (Profiles == null) Profiles = Profile.Defaults();
            Profiles.RemoveAll(p => p == null || string.IsNullOrEmpty(p.Id));
            foreach (var p in Profiles) if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Profile";
            if (ActiveProfileId != null && FindProfile(ActiveProfileId) == null) ActiveProfileId = null;
            foreach (var s in Subjects)
            {
                if (s.ProfileId != null && FindProfile(s.ProfileId) == null) s.ProfileId = null;
                if (s.Deadline != null && s.DeadlineDate == null) s.Deadline = null;
            }
            if (Subjects.Count > 0 && PlanSince == null) PlanSince = DateTime.Today.ToString("yyyy-MM-dd");
        }

        public Profile FindProfile(string id)
        {
            return id == null ? null : Profiles.FirstOrDefault(p => p.Id == id);
        }

        // The profile that describes the current switches (the last applied one first), or null.
        public Profile CurrentProfile()
        {
            var last = FindProfile(ActiveProfileId);
            if (last != null && last.Matches(this)) return last;
            return Profiles.FirstOrDefault(p => p.Matches(this));
        }

        // Next free colour slot (0..7); 8 = the neutral "other" colour once all are taken.
        public int NextColorIndex()
        {
            for (int i = 0; i < Theme.Series.Length; i++)
                if (!Subjects.Any(s => s.ColorIndex == i)) return i;
            return Theme.Series.Length;
        }
    }

    // Ready-made timer rhythms (shortcuts on the Settings page).
    class Rhythm
    {
        public readonly string Name;
        public readonly int Focus, ShortBreak, LongBreak, Sessions;

        Rhythm(string name, int focus, int shortBreak, int longBreak, int sessions)
        {
            Name = name; Focus = focus; ShortBreak = shortBreak; LongBreak = longBreak; Sessions = sessions;
        }

        public static readonly Rhythm[] All =
        {
            new Rhythm("Pomodoro 25", 25, 5, 15, 4),
            new Rhythm("Pomodoro 45", 45, 10, 20, 3),
            new Rhythm("Ultradian 90", 90, 20, 30, 2),
        };

        public static Rhythm Matching(AppSettings s)
        {
            return All.FirstOrDefault(r => r.Focus == s.FocusMinutes && r.ShortBreak == s.ShortBreakMinutes &&
                                           r.LongBreak == s.LongBreakMinutes && r.Sessions == s.SessionsBeforeLongBreak);
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
