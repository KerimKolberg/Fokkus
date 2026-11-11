using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Fokkus
{
    // ---- The shared sync file (same format on Windows and Android; lower-case names keep it small).

    public class SyncSubject
    {
        public string id { get; set; }
        public string name { get; set; }
        public int color { get; set; }
        public List<int> targets { get; set; }
        public string deadline { get; set; }
        public string deadlineLabel { get; set; }
        public string profileId { get; set; }
    }

    public class SyncPlan
    {
        public long updated { get; set; }        // when the plan was last changed (newest plan wins)
        public string planSince { get; set; }
        public bool autoNext { get; set; }
        public List<SyncSubject> subjects { get; set; }
    }

    public class SyncRecord
    {
        public long s { get; set; }      // start, Unix ms
        public int sec { get; set; }
        public string sub { get; set; }  // subject id
        public int p { get; set; }       // pomodoros (1 = completed)
        public string n { get; set; }    // note
        public long nt { get; set; }     // note time
    }

    public class SyncDevice
    {
        public string name { get; set; }
        public long seen { get; set; }
    }

    public class SyncDoc
    {
        public string app { get; set; }
        public int format { get; set; }
        public SyncPlan plan { get; set; }
        public List<SyncRecord> sessions { get; set; }
        public Dictionary<string, SyncDevice> devices { get; set; }

        public SyncDoc() { app = "Fokkus"; format = 1; sessions = new List<SyncRecord>(); devices = new Dictionary<string, SyncDevice>(); }

        public void Fix()
        {
            if (sessions == null) sessions = new List<SyncRecord>();
            if (devices == null) devices = new Dictionary<string, SyncDevice>();
            sessions.RemoveAll(r => r == null || r.sec <= 0);
            if (plan != null && plan.subjects == null) plan.subjects = new List<SyncSubject>();
        }
    }

    public class SyncOutcome
    {
        public int Received;        // records / notes taken from the file
        public int Sent;            // records / notes added to the file
        public bool PlanReceived, PlanSent, PlansJoined;
        public bool FileChanged;
    }

    // Merges this device's data with the shared file. Statistics and notes are joined (nothing is ever deleted);
    // for the study plan the newest version wins - the very first time, two separate plans are joined by subject name.
    static class SyncMerge
    {
        public static SyncOutcome Apply(AppSettings s, SessionStore store, SyncDoc doc, string deviceName, long nowMs)
        {
            var o = new SyncOutcome();
            doc.Fix();
            bool firstTime = s.LastSyncMs == 0;
            var remote = doc.plan;
            bool remoteHasPlan = remote != null && remote.subjects.Count > 0;

            long remoteUpdated = remote == null ? 0 : remote.updated;
            if (firstTime && remoteHasPlan && s.Subjects.Count > 0)
            {
                JoinPlans(s, store, remote);
                s.PlanSignature = s.ComputePlanSignature();
                s.PlanUpdatedMs = Math.Max(nowMs, remoteUpdated + 1);
                o.PlansJoined = true;
            }
            else if (firstTime && !remoteHasPlan && s.Subjects.Count > 0)
            {
                // An empty plan from another device never replaces yours.
                s.PlanUpdatedMs = Math.Max(s.PlanUpdatedMs, remoteUpdated + 1);
            }
            else if (remote != null && remote.updated > s.PlanUpdatedMs)
            {
                AdoptPlan(s, remote);
                o.PlanReceived = true;
            }
            if (remote == null || s.PlanUpdatedMs > remote.updated)
            {
                doc.plan = PlanOf(s);
                o.PlanSent = true;
                o.FileChanged = true;
            }

            // Statistics + notes, both ways.
            o.Received = store.Merge(doc.sessions.Select(ToRecord));
            var byKey = new Dictionary<string, SyncRecord>();
            foreach (var r in doc.sessions) byKey[r.s + "|" + r.sec] = r;
            foreach (var r in store.Records)
            {
                SyncRecord theirs;
                if (!byKey.TryGetValue(SessionStore.KeyOf(r), out theirs))
                {
                    var add = ToSync(r);
                    doc.sessions.Add(add);
                    byKey[SessionStore.KeyOf(r)] = add;
                    o.Sent++;
                }
                else if (r.NoteMs > theirs.nt)
                {
                    theirs.n = r.Note;
                    theirs.nt = r.NoteMs;
                    o.Sent++;
                }
            }
            if (o.Sent > 0)
            {
                doc.sessions.Sort((a, b) => a.s.CompareTo(b.s));
                o.FileChanged = true;
            }

            SyncDevice me;
            if (!doc.devices.TryGetValue(s.SyncDeviceId, out me) || o.FileChanged || nowMs - me.seen > 6 * 3600 * 1000L)
            {
                doc.devices[s.SyncDeviceId] = new SyncDevice { name = deviceName, seen = nowMs };
                o.FileChanged = true;
            }
            s.LastSyncMs = nowMs;
            return o;
        }

        static void JoinPlans(AppSettings s, SessionStore store, SyncPlan remote)
        {
            var joined = remote.subjects.Select(ToSubject).ToList();
            var map = new Dictionary<string, string>();
            foreach (var mine in s.Subjects)
            {
                var twin = joined.FirstOrDefault(x => string.Equals(x.Name.Trim(), mine.Name.Trim(), StringComparison.OrdinalIgnoreCase));
                if (twin != null)
                {
                    if (twin.Id != mine.Id) map[mine.Id] = twin.Id;
                    if (twin.Deadline == null && mine.Deadline != null) { twin.Deadline = mine.Deadline; twin.DeadlineLabel = mine.DeadlineLabel; }
                    if (twin.ProfileId == null) twin.ProfileId = mine.ProfileId;
                }
                else
                {
                    if (joined.Any(x => x.ColorIndex == mine.ColorIndex))
                        mine.ColorIndex = Enumerable.Range(0, Theme.Series.Length).Where(i => joined.All(x => x.ColorIndex != i)).DefaultIfEmpty(Theme.Series.Length).First();
                    joined.Add(mine);
                }
            }
            s.Subjects = joined;
            string active;
            if (s.ActiveSubjectId != null && map.TryGetValue(s.ActiveSubjectId, out active)) s.ActiveSubjectId = active;
            if (string.Compare(remote.planSince, s.PlanSince, StringComparison.Ordinal) < 0 || s.PlanSince == null) s.PlanSince = remote.planSince ?? s.PlanSince;
            store.RemapSubjects(map);
        }

        static void AdoptPlan(AppSettings s, SyncPlan remote)
        {
            s.Subjects = remote.subjects.Select(ToSubject).ToList();
            s.PlanSince = remote.planSince;
            s.AutoNextSubject = remote.autoNext;
            if (s.ActiveSubjectId != null && s.Subjects.All(x => x.Id != s.ActiveSubjectId)) s.ActiveSubjectId = null;
            s.PlanSignature = s.ComputePlanSignature();
            s.PlanUpdatedMs = remote.updated;
        }

        public static SyncPlan PlanOf(AppSettings s)
        {
            return new SyncPlan
            {
                updated = s.PlanUpdatedMs,
                planSince = s.PlanSince,
                autoNext = s.AutoNextSubject,
                subjects = s.Subjects.Select(x => new SyncSubject
                {
                    id = x.Id, name = x.Name, color = x.ColorIndex, targets = x.Targets.ToList(),
                    deadline = x.Deadline, deadlineLabel = x.DeadlineLabel, profileId = x.ProfileId,
                }).ToList(),
            };
        }

        static Subject ToSubject(SyncSubject x)
        {
            var t = (x.targets ?? new List<int>()).Take(7).ToList();
            while (t.Count < 7) t.Add(0);
            return new Subject
            {
                Id = string.IsNullOrEmpty(x.id) ? Guid.NewGuid().ToString("N").Substring(0, 10) : x.id,
                Name = string.IsNullOrWhiteSpace(x.name) ? "Subject" : x.name,
                ColorIndex = x.color, Targets = t,
                Deadline = x.deadline, DeadlineLabel = x.deadlineLabel, ProfileId = x.profileId,
            };
        }

        public static FocusRecord ToRecord(SyncRecord r)
        {
            return new FocusRecord { StartMs = r.s, Seconds = r.sec, SubjectId = r.sub, Pomodoros = r.p, Note = r.n, NoteMs = r.nt };
        }

        public static SyncRecord ToSync(FocusRecord r)
        {
            return new SyncRecord { s = r.StartMs, sec = r.Seconds, sub = r.SubjectId, p = r.Pomodoros, n = r.Note, nt = r.NoteMs };
        }

        // Joins two copies of the file (e.g. a conflicted copy the cloud app made).
        public static void Absorb(SyncDoc into, SyncDoc other)
        {
            other.Fix();
            var have = new Dictionary<string, SyncRecord>();
            foreach (var r in into.sessions) have[r.s + "|" + r.sec] = r;
            foreach (var r in other.sessions)
            {
                SyncRecord mine;
                if (!have.TryGetValue(r.s + "|" + r.sec, out mine)) { into.sessions.Add(r); have[r.s + "|" + r.sec] = r; }
                else if (r.nt > mine.nt) { mine.n = r.n; mine.nt = r.nt; }
            }
            if (other.plan != null && (into.plan == null || other.plan.updated > into.plan.updated)) into.plan = other.plan;
            foreach (var kv in other.devices)
                if (!into.devices.ContainsKey(kv.Key)) into.devices[kv.Key] = kv.Value;
        }
    }

    // Reads and writes Fokkus-sync.json in a folder your cloud app keeps in sync (Google Drive, OneDrive...).
    class SyncService
    {
        public const string FileName = "Fokkus-sync.json";
        readonly TrayContext ctx;
        public bool Busy { get; private set; }
        public string Status { get; private set; }
        public bool LastFailed { get; private set; }
        public event Action Changed;

        public SyncService(TrayContext ctx) { this.ctx = ctx; }

        static JavaScriptSerializer Json()
        {
            var js = new JavaScriptSerializer();
            js.MaxJsonLength = int.MaxValue;
            return js;
        }

        public static string PathIn(string folder) { return Path.Combine(folder, FileName); }

        public void Run()
        {
            string folder = ctx.Settings.SyncFolder;
            if (string.IsNullOrEmpty(folder) || Busy) return;
            Busy = true;
            Raise("Syncing…", false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                SyncDoc doc = null;
                string error = null;
                try
                {
                    if (!Directory.Exists(folder)) throw new IOException("The folder " + folder + " isn't there (is the cloud app running?).");
                    doc = ReadAll(folder);
                }
                catch (Exception ex) { error = ex.Message; Log.Error(ex); }
                ctx.OnUi(() => Merge(folder, doc, error));
            });
        }

        void Merge(string folder, SyncDoc doc, string error)
        {
            if (error != null) { Finish(false, "Sync problem: " + error); return; }
            var o = SyncMerge.Apply(ctx.Settings, ctx.Sessions, doc, "Windows · " + Environment.MachineName, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            ctx.Settings.Save();
            if (o.Received > 0 || o.PlanReceived || o.PlansJoined) ctx.DataChanged();
            string summary = Summary(o);
            if (!o.FileChanged) { Finish(true, summary); return; }
            string text = Json().Serialize(doc);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err = null;
                try { Write(folder, text); }
                catch (Exception ex) { err = ex.Message; Log.Error(ex); }
                ctx.OnUi(() => Finish(err == null, err == null ? summary : "Couldn't write the sync file: " + err));
            });
        }

        static string Summary(SyncOutcome o)
        {
            var parts = new List<string>();
            if (o.Received > 0) parts.Add(o.Received + " from your other devices");
            if (o.Sent > 0) parts.Add(o.Sent + " shared");
            if (o.PlansJoined) parts.Add("plans joined");
            else if (o.PlanReceived) parts.Add("plan updated");
            else if (o.PlanSent) parts.Add("plan shared");
            return "Synced " + DateTime.Now.ToString("HH:mm") + (parts.Count > 0 ? " · " + string.Join(", ", parts) : " · up to date");
        }

        void Finish(bool ok, string status)
        {
            Busy = false;
            Raise(status, !ok);
        }

        void Raise(string status, bool failed)
        {
            Status = status;
            LastFailed = failed;
            var h = Changed;
            if (h != null) h();
        }

        // The main file plus any conflicted copies the cloud app made ("Fokkus-sync (1).json"...).
        static SyncDoc ReadAll(string folder)
        {
            var doc = new SyncDoc();
            string main = PathIn(folder);
            foreach (var f in Directory.GetFiles(folder, "Fokkus-sync*.json").OrderBy(f => f == main ? 0 : 1))
            {
                string text = File.ReadAllText(f, Encoding.UTF8).TrimStart('﻿');
                if (text.Trim().Length == 0) continue;
                SyncDoc d;
                try { d = Json().Deserialize<SyncDoc>(text); }
                catch (Exception ex)
                {
                    // A damaged file is kept aside; the other devices put their data back on their next sync.
                    Log.Error(ex);
                    if (f == main) try { File.Move(f, Path.Combine(folder, "Fokkus-sync.damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt")); } catch { }
                    continue;
                }
                if (d == null) continue;
                if (d.app != null && d.app != "Fokkus") continue;
                SyncMerge.Absorb(doc, d);
            }
            return doc;
        }

        static void Write(string folder, string text)
        {
            string path = PathIn(folder), tmp = path + ".tmp";
            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(tmp, text, utf8);
            try
            {
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (IOException)
            {
                // Some cloud drives don't support replacing; fall back to a plain overwrite.
                File.Copy(tmp, path, true);
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
