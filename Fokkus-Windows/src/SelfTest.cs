using System;
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

            var allow = new HostRules(true, new[] { "tum.de", "example.com" });
            check("allow: subdomain", allow.IsAllowed("campus.tum.de"));
            check("allow: exact", allow.IsAllowed("tum.de"));
            check("allow: lookalike blocked", !allow.IsAllowed("eviltum.de"));
            check("allow: other blocked", !allow.IsAllowed("www.youtube.com"));
            check("allow: localhost/LAN", allow.IsAllowed("localhost") && allow.IsAllowed("192.168.178.1") && allow.IsAllowed("127.0.0.1"));
            var block = new HostRules(false, new[] { "youtube.com" });
            check("block: listed", !block.IsAllowed("m.youtube.com"));
            check("block: others ok", block.IsAllowed("moodle.tum.de"));
            check("distraction detection", Presets.IsKnownDistraction("www.instagram.com") && !Presets.IsKnownDistraction("moodle.tum.de"));
            check("bypass list", allow.BuildBypassList().Contains(";tum.de;*.tum.de") && allow.BuildBypassList().Contains(SystemProxy.Signature));

            // On/off switches + categories
            var sw = new AppSettings();
            check("GitHub & coding allowed by default", HostRules.From(sw).IsAllowed("github.githubassets.com") && HostRules.From(sw).IsAllowed("marketplace.visualstudio.com"));
            sw.DisabledAllowedSites.Add("github.com");
            check("switched-off site is blocked", !HostRules.From(sw).IsAllowed("github.com") && HostRules.From(sw).IsAllowed("moodle.tum.de"));
            var coding = Presets.AllowSites.First(p => p.Name == Presets.CodingPreset);
            check("category off while an entry is off", !Categories.IsOn(coding, sw.AllowedSites, sw.DisabledAllowedSites));
            Categories.Toggle(coding, Presets.AllowSites, sw.AllowedSites, sw.DisabledAllowedSites);
            check("category on switches entries back on", Categories.IsOn(coding, sw.AllowedSites, sw.DisabledAllowedSites) && HostRules.From(sw).IsAllowed("github.com"));
            Categories.Toggle(coding, Presets.AllowSites, sw.AllowedSites, sw.DisabledAllowedSites);
            check("category off keeps entries but blocks them", AppSettings.Has(sw.AllowedSites, "vscode.dev") && !HostRules.From(sw).IsAllowed("vscode.dev"));
            sw.DisabledApps.Add("Discord");
            check("switched-off app is not closed", !sw.ActiveApps.Contains("Discord") && sw.ActiveApps.Contains("steam"));

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
