using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Fokkus
{
    static class Program
    {
        const string MutexName = "Fokkus-single-instance-5c1e";
        const string ShowEventName = "Fokkus-show-5c1e";

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Contains("--restore-internet"))
            {
                bool silent = args.Contains("--silent");
                if (silent && IsRunning()) return 0;
                SystemProxy.RecoverAll();
                if (!silent) MessageBox.Show("Internet settings restored.", "Fokkus", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }
            if (args.Length >= 2 && args[0] == "--selftest") return SelfTest.Run(args[1]);
            if (args.Length >= 2 && args[0] == "--write-icon")
            {
                try
                {
                    IconFactory.WriteIco(args[1]);
                    if (args.Length >= 3) IconFactory.WritePng(args[2], 512);
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Error(ex);
                    return 2;
                }
            }

            if (args.Length >= 2 && args[0] == "--screenshots")
            {
                // Renders every screen to PNGs. Runs alongside a live Fokkus and never touches
                // the proxy, running apps or the settings file.
                try { Native.SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;
                TrayContext.Headless = true;
                AppSettings.ReadOnly = true;
                using (var unused = new EventWaitHandle(false, EventResetMode.AutoReset))
                {
                    var shots = new TrayContext(true, unused);
                    shots.CaptureScreens(args[1]);
                    shots.Exit();
                }
                return 0;
            }

            try { Native.SetProcessDPIAware(); } catch { }

            bool created;
            var mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                try { using (var ev = EventWaitHandle.OpenExisting(ShowEventName)) ev.Set(); } catch { }
                return 0;
            }
            var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) Theme.Scale = g.DpiX / 96f;

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => Log.Error(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { SystemProxy.Disengage(); } catch { }
                Log.Error(e.ExceptionObject as Exception);
            };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => { try { SystemProxy.Disengage(); } catch { } };

            SystemProxy.RecoverAll();
            try
            {
                Application.Run(new TrayContext(args.Contains("--minimized"), showEvent));
            }
            finally
            {
                try { SystemProxy.Disengage(); } catch { }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static bool IsRunning()
        {
            Mutex m;
            if (Mutex.TryOpenExisting(MutexName, out m)) { m.Dispose(); return true; }
            return false;
        }
    }
}
