using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace Fokkus
{
    // Export (CSV for Excel / Sheets) and full backups of settings, plan, statistics, notes and distractions.
    static class Backup
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static JavaScriptSerializer Json()
        {
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js;
        }

        static string Cell(string v)
        {
            v = v ?? "";
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        // One row per focus block: date, start, end, minutes, subject, completed pomodoro, note.
        public static string Csv(AppSettings s, SessionStore store)
        {
            var sb = new StringBuilder();
            sb.Append("date,start,end,minutes,subject,completed,note\r\n");
            foreach (var r in store.Records.OrderBy(x => x.StartMs))
            {
                DateTime a = SessionStore.StartOf(r), z = a.AddSeconds(r.Seconds);
                var subj = Planner.Find(s, r.SubjectId);
                sb.Append(a.ToString("yyyy-MM-dd", Inv)).Append(',')
                  .Append(a.ToString("HH:mm", Inv)).Append(',')
                  .Append(z.ToString("HH:mm", Inv)).Append(',')
                  .Append((r.Seconds / 60.0).ToString("0.#", Inv)).Append(',')
                  .Append(Cell(subj != null ? subj.Name : "")).Append(',')
                  .Append(r.Pomodoros > 0 ? "yes" : "no").Append(',')
                  .Append(Cell(r.Note)).Append("\r\n");
            }
            return sb.ToString();
        }

        public static string Create(AppSettings s, SessionStore store, DistractionLog log)
        {
            var d = new Dictionary<string, object>
            {
                { "app", "Fokkus" },
                { "kind", "backup" },
                { "platform", "windows" },
                { "format", 1 },
                { "created", DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv) },
                { "settings", s },
                { "sessions", store.Records },
                { "distractions", log.Entries },
            };
            return Json().Serialize(d);
        }

        public class Parsed
        {
            public string Created, Platform;
            public AppSettings Settings;        // null for a backup from the phone
            public List<FocusRecord> Sessions = new List<FocusRecord>();
            public List<DistractionEntry> Distractions = new List<DistractionEntry>();
        }

        // Reads a Fokkus backup (Windows or Android). Throws with a readable message if it isn't one.
        public static Parsed Parse(string text)
        {
            var js = Json();
            var d = js.Deserialize<Dictionary<string, object>>(text.TrimStart('﻿'));
            object app;
            if (d == null || !d.TryGetValue("app", out app) || !"Fokkus".Equals(app)) throw new InvalidDataException("This isn't a Fokkus backup.");
            var p = new Parsed();
            object v;
            p.Created = d.TryGetValue("created", out v) ? Convert.ToString(v, Inv) : "?";
            p.Platform = d.TryGetValue("platform", out v) ? Convert.ToString(v, Inv) : "windows";
            if (p.Platform == "windows" && d.TryGetValue("settings", out v) && v != null) p.Settings = js.ConvertToType<AppSettings>(v);
            if (d.TryGetValue("sessions", out v) && v is IEnumerable)
                foreach (var item in (IEnumerable)v)
                {
                    var m = item as IDictionary<string, object>;
                    if (m == null) continue;
                    // Windows backups use the record's own names, phone backups the short sync names.
                    p.Sessions.Add(m.ContainsKey("StartMs") ? js.ConvertToType<FocusRecord>(m) : SyncMerge.ToRecord(js.ConvertToType<SyncRecord>(m)));
                }
            if (d.TryGetValue("distractions", out v) && v is IEnumerable)
                foreach (var item in (IEnumerable)v)
                {
                    var m = item as IDictionary<string, object>;
                    if (m != null) p.Distractions.Add(js.ConvertToType<DistractionEntry>(m));
                }
            return p;
        }

        // Settings that belong to this computer and are kept when restoring.
        static readonly HashSet<string> KeepLocal = new HashSet<string> { "SyncFolder", "SyncDeviceId", "LastSyncMs", "MiniX", "MiniY", "StartWithWindows" };

        // Restores settings + plan (Windows backups) and adds the statistics, notes and distractions you don't have.
        public static int Restore(Parsed p, AppSettings into, SessionStore store, DistractionLog log)
        {
            if (p.Settings != null)
                foreach (var prop in typeof(AppSettings).GetProperties())
                    if (prop.CanRead && prop.CanWrite && !KeepLocal.Contains(prop.Name) && prop.GetIndexParameters().Length == 0)
                        prop.SetValue(into, prop.GetValue(p.Settings, null), null);
            // A restored plan counts as your newest one (otherwise the next sync would bring the old one back).
            into.PlanSignature = null;
            int added = store.Merge(p.Sessions);
            log.Merge(p.Distractions);
            return added;
        }

        // CSV with a BOM so Excel reads umlauts correctly; JSON without one.
        public static void WriteText(string path, string text, bool bom) { File.WriteAllText(path, text, new UTF8Encoding(bom)); }
    }
}
