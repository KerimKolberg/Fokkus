using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Fokkus
{
    // "Allowed apps": while a focus session runs, a program that isn't on the allowed list is minimized
    // as soon as it comes to the front. Nothing is closed, so no work is lost.
    class AppGuard
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);

        const int SW_MINIMIZE = 6;
        const uint GA_ROOTOWNER = 3;

        // Windows itself, sign-in / permission prompts and installers always keep working.
        static readonly HashSet<string> Essentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Fokkus", "explorer", "ApplicationFrameHost", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp",
            "ShellHost", "TextInputHost", "LockApp", "LogonUI", "Taskmgr", "SystemSettings", "consent", "CredentialUIBroker", "dwm",
            "csrss", "winlogon", "smartscreen", "msiexec", "SnippingTool", "ScreenClippingHost", "PickerHost", "OpenWith", "SecHealthUI",
            "mmc", "rundll32", "dllhost", "WerFault", "UserOOBEBroker", "Magnify", "osk", "Narrator"
        };

        public static bool IsEssential(string name)
        {
            return Essentials.Contains(name) || name.StartsWith("Fokkus", StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith("unins", StringComparison.OrdinalIgnoreCase);
        }

        readonly Timer timer = new Timer();
        readonly uint self = (uint)Process.GetCurrentProcess().Id;
        HashSet<string> allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IntPtr lastOk;
        bool active;

        public event Action<string> AppMinimized; // raised on the UI thread

        public AppGuard()
        {
            timer.Interval = 700;
            timer.Tick += (s, e) => Check();
        }

        public void Update(bool on, IEnumerable<string> list)
        {
            allowed = new HashSet<string>(list.Select(AppBlocker.CleanName).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);
            lastOk = IntPtr.Zero;
            active = on;
            if (on && !timer.Enabled) timer.Start();
            else if (!on && timer.Enabled) timer.Stop();
        }

        public bool Allows(string name) { return IsEssential(name) || allowed.Contains(name); }

        void Check()
        {
            if (!active) return;
            try
            {
                IntPtr fg = GetForegroundWindow(), h = fg;
                if (fg == IntPtr.Zero || fg == lastOk || IsIconic(fg)) return;
                IntPtr root = GetAncestor(fg, GA_ROOTOWNER);
                if (root != IntPtr.Zero) h = root;
                string name = NameOf(h);
                if (name == null || Allows(name)) { lastOk = fg; return; }
                ShowWindow(h, SW_MINIMIZE);
                var ev = AppMinimized;
                if (ev != null) ev(name);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // Process name behind a window. Store apps live inside ApplicationFrameHost, so use the hosted app's name.
        string NameOf(IntPtr h)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid == 0 || pid == self) return null;
            string name = ProcessName(pid);
            if (name != null && name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
            {
                uint inner = 0;
                EnumChildWindows(h, (c, l) =>
                {
                    uint p;
                    GetWindowThreadProcessId(c, out p);
                    if (p != 0 && p != pid) { inner = p; return false; }
                    return true;
                }, IntPtr.Zero);
                if (inner != 0) name = ProcessName(inner) ?? name;
            }
            return name;
        }

        static string ProcessName(uint pid)
        {
            try { using (var p = Process.GetProcessById((int)pid)) return p.ProcessName; }
            catch { return null; }
        }
    }
}
