using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Fokkus
{
    // Closes blocked programs while a focus session runs: polite close first, then terminate.
    class AppBlocker
    {
        readonly Control invoker;
        readonly System.Threading.Timer timer;
        readonly Dictionary<int, DateTime> closeRequested = new Dictionary<int, DateTime>();
        readonly HashSet<int> reported = new HashSet<int>();
        volatile bool active;
        volatile string[] names = new string[0];
        int busy;

        public event Action<string> AppClosed; // raised on the UI thread

        public AppBlocker(Control invoker)
        {
            this.invoker = invoker;
            timer = new System.Threading.Timer(_ => Check(), null, 2000, 2000);
        }

        public static string CleanName(string n)
        {
            n = (n ?? "").Trim().Trim('"');
            if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n.Substring(0, n.Length - 4);
            if (n.IndexOf('\\') >= 0) n = n.Substring(n.LastIndexOf('\\') + 1);
            return n.Trim();
        }

        public void Update(bool on, IEnumerable<string> list)
        {
            names = list.Select(CleanName).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            bool was = active;
            active = on;
            if (on && !was) ThreadPool.QueueUserWorkItem(_ => Check());
            if (!on)
                lock (closeRequested) { closeRequested.Clear(); reported.Clear(); }
        }

        void Check()
        {
            if (!active) return;
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                int self = Process.GetCurrentProcess().Id;
                foreach (var name in names)
                {
                    Process[] procs;
                    try { procs = Process.GetProcessesByName(name); }
                    catch { continue; }
                    foreach (var p in procs)
                    {
                        try
                        {
                            if (p.Id == self || !active) continue;
                            DateTime asked;
                            bool hasAsked;
                            lock (closeRequested) hasAsked = closeRequested.TryGetValue(p.Id, out asked);
                            if (!hasAsked)
                            {
                                lock (closeRequested) closeRequested[p.Id] = DateTime.UtcNow;
                                bool polite = false;
                                try { polite = p.MainWindowHandle != IntPtr.Zero && p.CloseMainWindow(); } catch { }
                                if (!polite) p.Kill();
                                Report(p.Id, name);
                            }
                            else if ((DateTime.UtcNow - asked).TotalSeconds > 3)
                            {
                                p.Kill();
                            }
                        }
                        catch { }
                        finally { p.Dispose(); }
                    }
                }
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        void Report(int pid, string name)
        {
            lock (closeRequested) { if (!reported.Add(pid)) return; }
            var h = AppClosed;
            if (h == null) return;
            try { invoker.BeginInvoke(new Action(() => h(name))); } catch { }
        }

        public static List<string> RunningWindowApps()
        {
            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Fokkus", "kk-Lock-in", "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost",
                "SearchHost", "StartMenuExperienceHost", "LockApp", "dwm", "Taskmgr"
            };
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle) && !skip.Contains(p.ProcessName))
                        result.Add(p.ProcessName);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
