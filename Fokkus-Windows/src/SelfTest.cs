using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace Fokkus
{
    // "Fokkus.exe --selftest <file>": checks the domain rules and the filtering proxy
    // (requests are sent through the proxy explicitly; system settings are only read, never changed).
    static class SelfTest
    {
        public static int Run(string outFile)
        {
            AppSettings.ReadOnly = true;   // the tests never touch your real settings
            var sb = new StringBuilder();
            int failed = 0;
            Action<string, bool> check = (name, ok) =>
            {
                sb.AppendLine((ok ? "PASS  " : "FAIL  ") + name);
                if (!ok) failed++;
            };

            check("normalize url", HostRules.Normalize("https://www.Moodle.TUM.de/course/view.php?id=1") == "moodle.tum.de");
            check("normalize wildcard", HostRules.Normalize("*.tum.de") == "tum.de");
            check("normalize port", HostRules.Normalize("localhost:8080") == "localhost");
            check("normalize junk", HostRules.Normalize("not a domain") == null);
            check("base domain", HostRules.BaseDomain("i.ytimg.com") == "ytimg.com" && HostRules.BaseDomain("www.bbc.co.uk") == "bbc.co.uk");

            // "Allowed websites" only
            var allow = new HostRules(true, new[] { "tum.de", "example.com" }, false, new[] { "youtube.com" });
            check("allowed: subdomain", allow.IsAllowed("campus.tum.de"));
            check("allowed: exact", allow.IsAllowed("tum.de"));
            check("allowed: lookalike blocked", !allow.IsAllowed("eviltum.de"));
            check("allowed: other blocked", !allow.IsAllowed("www.wikipedia.org"));
            check("allowed: localhost/LAN", allow.IsAllowed("localhost") && allow.IsAllowed("192.168.178.1") && allow.IsAllowed("127.0.0.1"));
            check("bypass list", allow.BuildBypassList().Contains(";tum.de;*.tum.de") && allow.BuildBypassList().Contains(SystemProxy.Signature));

            // "Blocked websites" only: everything else works
            var block = new HostRules(false, new[] { "tum.de" }, true, new[] { "youtube.com" });
            check("blocked: listed", !block.IsAllowed("m.youtube.com"));
            check("blocked: everything else works", block.IsAllowed("moodle.tum.de") && block.IsAllowed("www.wikipedia.org"));
            check("blocked: allowed list not in bypass", !block.BuildBypassList().Contains("tum.de"));

            // Both on: only allowed sites work, and blocked ones never do (even when also allowed)
            var both = new HostRules(true, new[] { "google.com", "tum.de" }, true, new[] { "youtube.com", "mail.google.com" });
            check("both: allowed works", both.IsAllowed("docs.google.com") && both.IsAllowed("tum.de"));
            check("both: blocked wins over allowed", !both.IsAllowed("mail.google.com"));
            check("both: unlisted blocked", !both.IsAllowed("www.youtube.com") && !both.IsAllowed("reddit.com"));
            check("both: overlapping allowed rule not bypassed", !both.BuildBypassList().Contains(";google.com") && both.BuildBypassList().Contains(";tum.de"));

            // Both off: nothing filtered
            var none = new HostRules(false, new[] { "tum.de" }, false, new[] { "youtube.com" });
            check("both off: nothing filtered", !none.Active && none.IsAllowed("www.youtube.com"));
            check("distraction detection", Presets.IsKnownDistraction("www.instagram.com") && !Presets.IsKnownDistraction("moodle.tum.de"));

            // Defaults + upgrade of settings saved by v1.1
            var sw = new AppSettings();
            check("defaults: allowed websites on, blocked apps on", sw.AllowSitesOn && !sw.BlockSitesOn && !sw.AllowAppsOn && sw.BlockAppsOn);
            check("GitHub & coding allowed by default", HostRules.From(sw).IsAllowed("github.githubassets.com") && HostRules.From(sw).IsAllowed("marketplace.visualstudio.com"));
            check("VS Code + Claude allowed apps by default", AppSettings.Has(sw.AllowedApps, "Code") && AppSettings.Has(sw.AllowedApps, "Claude"));
            var old = new AppSettings();
            old.SettingsVersion = 2;
            old.BlockWebsites = true;
            old.AllowListMode = false;
            old.BlockApps = false;
            old.DisabledBlockedSites.Add("reddit.com");
            old.Upgrade();
            check("upgrade: block mode becomes blocked websites", !old.AllowSitesOn && old.BlockSitesOn && !old.BlockAppsOn && !old.AllowAppsOn);
            check("upgrade: switched-off entries removed", !AppSettings.Has(old.BlockedSites, "reddit.com") && old.DisabledBlockedSites.Count == 0 && old.SettingsVersion == AppSettings.CurrentVersion);
            check("YouTube allowed and not blocked by default", HostRules.From(sw).IsAllowed("www.youtube.com") && HostRules.From(sw).IsAllowed("rr3---sn-4g5e6nzs.googlevideo.com") &&
                  !sw.BlockedSites.Any(x => x.Contains("youtube")));
            var v3 = new AppSettings();
            v3.SettingsVersion = 3;
            v3.AllowSitesOn = true; v3.BlockSitesOn = true;
            v3.BlockedSites.AddRange(new[] { "youtube.com", "m.youtube.com", "ytimg.com", "googlevideo.com" });
            v3.AllowedSites.RemoveAll(x => Presets.YouTubeHosts.Contains(x));
            v3.Upgrade();
            check("upgrade: YouTube moves from blocked to allowed", HostRules.From(v3).IsAllowed("music.youtube.com") && HostRules.From(v3).IsAllowed("i.ytimg.com") &&
                  !HostRules.From(v3).IsAllowed("www.netflix.com"));
            var v4 = new AppSettings();
            v4.SettingsVersion = 4;
            v4.CloseToTray = false;
            v4.Upgrade();
            check("upgrade: minimizing keeps its old behavior", !v4.MinimizeToTray && new AppSettings().MinimizeToTray);
            // Session profiles
            var ps = new AppSettings();
            var coding = ps.Profiles.First(p => p.Name == "Programming");
            check("profiles: defaults", ps.Profiles.Count == 4 && ps.CurrentProfile() != null && ps.CurrentProfile().Name == "Study");
            coding.ApplyTo(ps);
            check("profiles: Programming = blocked websites only", !ps.AllowSitesOn && ps.BlockSitesOn && ps.BlockAppsOn && ps.CurrentProfile() == coding);
            var exam = ps.Profiles.First(p => p.Name == "Exam prep");
            check("profiles: loosening detected", !exam.Loosens(ps) && coding.Loosens(new AppSettings { StrictMode = true }));
            ps.BlockAppsOn = false;
            check("profiles: changed switches = custom", ps.CurrentProfile() == null);

            // Deadlines
            var ds = new AppSettings();
            var dstore = new SessionStore();
            dstore.ReadOnly = true;
            var subjA = new Subject { Name = "A", Targets = Enumerable.Repeat(1, 7).ToList() };
            var subjB = new Subject { Name = "B", Targets = Enumerable.Repeat(1, 7).ToList(), Deadline = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd") };
            var subjC = new Subject { Name = "C", Targets = Enumerable.Repeat(0, 7).ToList(), Deadline = DateTime.Today.AddDays(6).ToString("yyyy-MM-dd") };
            var past = new Subject { Name = "D", Deadline = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd") };
            ds.Subjects.AddRange(new[] { subjA, subjB, subjC, past });
            check("deadlines: days left", Planner.DaysLeft(subjB, DateTime.Today) == 2 && Planner.DaysLeft(past, DateTime.Today) == null && Planner.DaysLeft(subjA, DateTime.Today) == null);
            check("deadlines: extra pomodoros", Planner.ExtraFor(10) == 0 && Planner.ExtraFor(6) == 1 && Planner.ExtraFor(2) == 2);
            var drows = Planner.Today(ds, dstore);
            check("deadlines: close deadline shows up in today's plan", drows.Any(r => r.Subject == subjC && r.Extra == 1) && drows.First(r => r.Subject == subjB).Extra == 2);
            check("deadlines: nearest deadline first", Planner.NextPending(ds, dstore, null) == subjB && Planner.NextPending(ds, dstore, subjA.Id) == subjA);
            int dleft;
            check("deadlines: next deadline", Planner.NextDeadline(ds, out dleft) == subjB && dleft == 2 && Planner.DeadlineText(subjB, 2) == "Deadline in 2 days");

            // Notes + joining statistics (sync / restore)
            var st1 = new SessionStore { ReadOnly = true };
            var recA = new FocusRecord { StartMs = 1000000, Seconds = 1500, Pomodoros = 1 };
            st1.Add(recA);
            st1.SetNote(recA, "  Sheet 3 done ");
            check("notes: saved", recA.Note == "Sheet 3 done" && recA.NoteMs > 0);
            var st2 = new SessionStore { ReadOnly = true };
            st2.Add(new FocusRecord { StartMs = 1000000, Seconds = 1500, Pomodoros = 1 });
            st2.Add(new FocusRecord { StartMs = 2000000, Seconds = 600 });
            int got = st2.Merge(st1.Records);
            check("merge: newer note taken, no duplicates", got == 1 && st2.Records.Count == 2 && st2.Records[0].Note == "Sheet 3 done");
            check("merge: missing records added", st1.Merge(st2.Records) == 1 && st1.Records.Count == 2);

            // Sync between a PC and a phone that made their plans separately
            var pc = new AppSettings();
            var pcStore = new SessionStore { ReadOnly = true };
            pc.SyncDeviceId = "win-test";
            pc.Subjects.Add(new Subject { Id = "pc-ana", Name = "Analysis", ColorIndex = 0, Targets = Enumerable.Repeat(2, 7).ToList() });
            pc.Subjects.Add(new Subject { Id = "pc-phy", Name = "Physics", ColorIndex = 1, Targets = Enumerable.Repeat(1, 7).ToList() });
            pc.PlanUpdatedMs = 1000;
            pcStore.Add(new FocusRecord { StartMs = 5000000, Seconds = 1500, SubjectId = "pc-ana", Pomodoros = 1 });
            var doc = new SyncDoc();
            var o1 = SyncMerge.Apply(pc, pcStore, doc, "PC", 2000);
            check("sync: first device shares plan + stats", o1.PlanSent && o1.Sent == 1 && doc.plan.subjects.Count == 2 && doc.sessions.Count == 1);
            var phone = new AppSettings();
            var phStore = new SessionStore { ReadOnly = true };
            phone.SyncDeviceId = "and-test";
            phone.Subjects.Add(new Subject { Id = "ph-ana", Name = "analysis", ColorIndex = 0, Targets = Enumerable.Repeat(1, 7).ToList() });
            phone.Subjects.Add(new Subject { Id = "ph-lin", Name = "Linear Algebra", ColorIndex = 1, Targets = Enumerable.Repeat(1, 7).ToList() });
            phone.PlanUpdatedMs = 3000;
            phStore.Add(new FocusRecord { StartMs = 6000000, Seconds = 1500, SubjectId = "ph-ana", Pomodoros = 1 });
            var o2 = SyncMerge.Apply(phone, phStore, doc, "Phone", 4000);
            check("sync: first sync joins plans by name", o2.PlansJoined && phone.Subjects.Count == 3 && phone.Subjects.Any(x => x.Id == "pc-ana") && phone.Subjects.Any(x => x.Name == "Linear Algebra"));
            check("sync: records follow the joined subject", phStore.Records.Count == 2 && phStore.Records.All(r => r.SubjectId == "pc-ana"));
            check("sync: subject colours stay unique", phone.Subjects.Select(x => x.ColorIndex).Distinct().Count() == 3);
            var o3 = SyncMerge.Apply(pc, pcStore, doc, "PC", 5000);
            check("sync: other device gets stats + newer plan", o3.Received == 1 && o3.PlanReceived && pc.Subjects.Count == 3 && pcStore.Records.Count == 2);
            check("sync: nothing new = nothing written", !SyncMerge.Apply(pc, pcStore, doc, "PC", 6000).FileChanged);
            var emptyPlan = new SyncDoc();
            emptyPlan.plan = new SyncPlan { updated = 9999999, subjects = new List<SyncSubject>() };
            var keep = new AppSettings();
            keep.SyncDeviceId = "keep";
            keep.Subjects.Add(new Subject { Name = "Keep me" });
            keep.PlanUpdatedMs = 10;
            SyncMerge.Apply(keep, new SessionStore { ReadOnly = true }, emptyPlan, "K", 20);
            check("sync: an empty plan never wipes yours", keep.Subjects.Count == 1 && emptyPlan.plan.subjects.Count == 1);

            // Smarter breaks
            check("smart breaks", Breaks.Suggest(25, 5, 0) == 0 && Breaks.Suggest(60, 5, 0) == 15 && Breaks.Suggest(90, 20, 0) == 0 &&
                                  Breaks.Suggest(25, 5, 2) == 10 && Breaks.Suggest(50, 10, 0) == 0);
            var eng = new PomodoroEngine(() => new AppSettings());
            eng.Start();
            eng.SetPhaseLength(TimeSpan.FromMinutes(40));
            check("longer phase", Math.Abs(eng.Remaining.TotalMinutes - 40) < 0.1 && Math.Abs(eng.Total.TotalMinutes - 40) < 0.01);

            // Distraction insights
            var dl = new DistractionLog { ReadOnly = true };
            var at14 = DateTime.Today.AddHours(14);
            dl.Add("site", "youtube.com", at14);
            dl.Add("site", "youtube.com", at14.AddSeconds(20));
            dl.Add("site", "youtube.com", at14.AddMinutes(5));
            dl.Add("app", "Discord", at14.AddMinutes(6));
            dl.Add("site", "reddit.com", DateTime.Today.AddHours(9));
            var top = Insights.Top(dl, DateTime.Today, DateTime.Today.AddDays(1), 5);
            check("insights: one attempt per minute", dl.Entries.Count == 4);
            check("insights: top distraction + its hour", top.Count == 3 && top[0].Name == "youtube.com" && top[0].Count == 2 && top[0].PeakHour == 14);

            // Weekly review
            var rstore = new SessionStore { ReadOnly = true };
            var lastMonday = Analytics.WeekStart(DateTime.Today).AddDays(-7);
            rstore.Add(new FocusRecord { StartMs = SessionStore.ToUnixMs(lastMonday.AddHours(9)), Seconds = 3000, Pomodoros = 1, Note = "Sheet 1" });
            rstore.Add(new FocusRecord { StartMs = SessionStore.ToUnixMs(lastMonday.AddDays(1).AddHours(10)), Seconds = 1500, Pomodoros = 1 });
            var rev = Review.Compute(new AppSettings(), rstore, dl, lastMonday);
            check("weekly review", Math.Abs(rev.Stats.TotalMinutes - 75) < 0.1 && rev.BestHour == 9 && rev.Notes.Count == 1 && rev.BestDay == lastMonday);
            check("weekly review: which week", Review.DueWeek(new DateTime(2026, 11, 15, 19, 0, 0)) == new DateTime(2026, 11, 9) &&
                                               Review.DueWeek(new DateTime(2026, 11, 11, 9, 0, 0)) == new DateTime(2026, 11, 2));

            // Export + backup
            var csvStore = new SessionStore { ReadOnly = true };
            csvStore.Add(new FocusRecord { StartMs = SessionStore.ToUnixMs(new DateTime(2026, 11, 10, 9, 0, 0)), Seconds = 1500, Pomodoros = 1, SubjectId = "pc-ana", Note = "Sheet 3, \"task\" 2" });
            string csv = Backup.Csv(pc, csvStore);
            check("csv export", csv.StartsWith("date,start,end,minutes,subject,completed,note") &&
                                csv.Contains("2026-11-10,09:00,09:25,25,Analysis,yes,\"Sheet 3, \"\"task\"\" 2\""));
            var parsed = Backup.Parse(Backup.Create(pc, pcStore, dl));
            check("backup round trip", parsed.Settings != null && parsed.Settings.Subjects.Count == pc.Subjects.Count &&
                                       parsed.Sessions.Count == pcStore.Records.Count && parsed.Distractions.Count == dl.Entries.Count);
            var target = new AppSettings();
            target.SyncFolder = "keep-me";
            int restored = Backup.Restore(parsed, target, new SessionStore { ReadOnly = true }, new DistractionLog { ReadOnly = true });
            check("restore: settings + stats, keeps this PC's sync folder", restored == pcStore.Records.Count && target.Subjects.Count == pc.Subjects.Count && target.SyncFolder == "keep-me");

            // Focus music
            var wav = Noise.BrownNoiseWav(2, 8000);
            check("brown noise file", wav.Length == 44 + 2 * 8000 * 2 && wav[0] == 'R' && wav[8] == 'W');
            var ms = new AppSettings();
            ms.MusicOn = true;
            check("music stream gets through the website filter", HostRules.From(ms).IsAllowed("ice6.somafm.com") && !HostRules.From(new AppSettings()).IsAllowed("ice6.somafm.com"));

            check("timer rhythms", Rhythm.Matching(new AppSettings()) == Rhythm.All[0] && Rhythm.All.Select(r => r.Focus).SequenceEqual(new[] { 25, 45, 90 }));

            // Allowed apps
            var guard = new AppGuard();
            guard.Update(true, new[] { "Code", "chrome.exe" });
            check("allowed apps: listed and Windows essentials stay", guard.Allows("code") && guard.Allows("chrome") && guard.Allows("explorer") && guard.Allows("Fokkus"));
            check("allowed apps: others minimized", !guard.Allows("Discord") && !guard.Allows("steam"));
            guard.Update(false, new string[0]);

            // Study plan + statistics
            check("weekday index", Planner.DayIndex(new DateTime(2026, 10, 5)) == 0 && Planner.DayIndex(new DateTime(2026, 10, 4)) == 6);
            var st = new AppSettings();
            var subj = new Subject();
            subj.Name = "Analysis";
            subj.ColorIndex = 0;
            subj.Targets = new System.Collections.Generic.List<int> { 2, 2, 2, 2, 2, 2, 2 };
            st.Subjects.Add(subj);
            st.PlanSince = Analytics.WeekStart(DateTime.Today).ToString("yyyy-MM-dd");
            var store = new SessionStore();
            store.ReadOnly = true;
            var t0 = DateTime.Today.AddHours(10).AddMinutes(50);
            store.Add(new FocusRecord { StartMs = SessionStore.ToUnixMs(t0), Seconds = 1200, SubjectId = subj.Id, Pomodoros = 1 });
            store.Add(new FocusRecord { StartMs = SessionStore.ToUnixMs(DateTime.Today.AddHours(14)), Seconds = 600, SubjectId = null, Pomodoros = 0 });
            var day = Analytics.Compute(st, store, StatRange.Day, DateTime.Today);
            check("day chart splits a session across hours", Math.Abs(day.Buckets[10].Total - 10) < 0.01 && Math.Abs(day.Buckets[11].Total - 10) < 0.01);
            check("day totals + pomodoros", Math.Abs(day.TotalMinutes - 30) < 0.01 && day.Pomodoros == 1);
            check("unassigned time goes to Other", Math.Abs(day.Buckets[14].Minutes[day.Series.Count - 1] - 10) < 0.01);
            var week = Analytics.Compute(st, store, StatRange.Week, DateTime.Today);
            check("week has 7 buckets with plan targets", week.Buckets.Count == 7 && week.Buckets.All(b => Math.Abs(b.Target - 2 * st.FocusMinutes) < 0.01));
            var month = Analytics.Compute(st, store, StatRange.Month, DateTime.Today);
            var year = Analytics.Compute(st, store, StatRange.Year, DateTime.Today);
            check("month/year totals agree", Math.Abs(month.TotalMinutes - 30) < 0.01 && Math.Abs(year.TotalMinutes - 30) < 0.01 && year.Buckets.Count == 12);
            check("today's plan progress", Planner.Today(st, store).Single().Done == 1 && Planner.NextPending(st, store, null) == subj);
            check("streak", store.Streak() == 1);
            check("compact durations", Theme.Compact(45) == "45m" && Theme.Compact(200) == "3h 20m" && Theme.Compact(120) == "2h");

            var q = SystemProxy.Query();
            sb.AppendLine("INFO  current system proxy: flags=" + q.Flags + " server=" + (q.Server ?? "(none)") + " bypass=" + (q.Bypass ?? "(none)") + " pac=" + (q.AutoConfigUrl ?? "(none)"));
            check("system proxy readable", q.Flags != 0);

            var proxy = new ProxyServer();
            proxy.Rules = allow;
            int blockedEvents = 0;
            proxy.Blocked += h => System.Threading.Interlocked.Increment(ref blockedEvents);
            proxy.Start();
            sb.AppendLine("INFO  proxy port " + proxy.Port);
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; // TLS 1.2 + 1.3

            check("https allowed site passes (example.com)", Fetch("https://example.com/", proxy.Port, sb) == 200);
            check("https allowed subdomain passes (www.tum.de)", Fetch("https://www.tum.de/", proxy.Port, sb) == 200);
            check("http allowed site passes (example.com)", Fetch("http://example.com/", proxy.Port, sb) == 200);
            check("https blocked site refused (youtube.com)", Fetch("https://www.youtube.com/", proxy.Port, sb) == 403);
            check("http blocked site gets block page", Fetch("http://neverssl.com/", proxy.Port, sb) == 403);
            check("blocked events raised", blockedEvents >= 2);
            proxy.Rules = block;
            check("blocked-only: other https site passes (example.com)", Fetch("https://example.com/", proxy.Port, sb) == 200);
            check("blocked-only: blocked site refused (youtube.com)", Fetch("https://www.youtube.com/", proxy.Port, sb) == 403);
            proxy.Stop();

            sb.AppendLine(failed == 0 ? "ALL PASSED" : failed + " FAILED");
            File.WriteAllText(outFile, sb.ToString());
            return failed;
        }

        static int Fetch(string url, int port, StringBuilder log)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Proxy = new WebProxy("127.0.0.1", port);
                req.Timeout = 15000;
                req.AllowAutoRedirect = false;
                req.UserAgent = "Fokkus-selftest";
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    log.AppendLine("INFO  " + url + " -> " + (int)resp.StatusCode);
                    return (int)resp.StatusCode < 400 ? 200 : (int)resp.StatusCode;
                }
            }
            catch (WebException ex)
            {
                var r = ex.Response as HttpWebResponse;
                int code = r != null ? (int)r.StatusCode : -1;
                if (code == -1 && ex.Message.Contains("403")) code = 403;
                log.AppendLine("INFO  " + url + " -> " + code + " (" + ex.Status + ": " + ex.Message + ")");
                return code;
            }
            catch (Exception ex)
            {
                log.AppendLine("INFO  " + url + " -> error " + ex.Message);
                return -1;
            }
        }
    }
}
