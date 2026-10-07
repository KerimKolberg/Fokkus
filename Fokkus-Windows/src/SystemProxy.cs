using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace Fokkus
{
    public class ProxyBackup
    {
        public int Flags { get; set; }
        public string Server { get; set; }
        public string Bypass { get; set; }
        public string AutoConfigUrl { get; set; }
    }

    // Points the per-user Windows (WinINet) proxy at Fokkus during focus and restores the previous
    // settings afterwards. Chrome, Edge, Brave, Opera and Firefox ("use system proxy") all follow it.
    static class SystemProxy
    {
        const int INTERNET_OPTION_PER_CONNECTION_OPTION = 75;
        const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        const int INTERNET_OPTION_REFRESH = 37;
        const int INTERNET_OPTION_PROXY_SETTINGS_CHANGED = 95;
        const int PER_CONN_FLAGS = 1, PER_CONN_PROXY_SERVER = 2, PER_CONN_PROXY_BYPASS = 3, PER_CONN_AUTOCONFIG_URL = 4, PER_CONN_FLAGS_UI = 10;
        const int PROXY_TYPE_DIRECT = 1, PROXY_TYPE_PROXY = 2, PROXY_TYPE_AUTO_DETECT = 8;
        const string RunOnceKey = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
        const string RunOnceName = "Fokkus-restore";
        const string LegacyRunOnceName = "kk-Lock-in-restore";

        // Marker placed in the bypass list so stale Fokkus settings can always be recognised.
        public const string Signature = "fokkus.local";
        const string LegacySignature = "kk-lock-in.local"; // the app's old name

        static bool IsOurs(string bypass)
        {
            return bypass != null && (bypass.Contains(Signature) || bypass.Contains(LegacySignature));
        }

        [StructLayout(LayoutKind.Sequential)]
        struct OptionList
        {
            public int dwSize;
            public IntPtr pszConnection;
            public int dwOptionCount;
            public int dwOptionError;
            public IntPtr pOptions;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct OptionValue
        {
            [FieldOffset(0)] public int dwValue;
            [FieldOffset(0)] public IntPtr pszValue;
            [FieldOffset(0)] public int ftLow;
            [FieldOffset(4)] public int ftHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Option
        {
            public int dwOption;
            public OptionValue Value;
        }

        [DllImport("wininet.dll", SetLastError = true, EntryPoint = "InternetSetOptionW")]
        static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        [DllImport("wininet.dll", SetLastError = true, EntryPoint = "InternetQueryOptionW")]
        static extern bool InternetQueryOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, ref int lpdwBufferLength);

        [DllImport("kernel32.dll")]
        static extern IntPtr GlobalFree(IntPtr hMem);

        static readonly object sync = new object();
        static string lastApplied;

        static string BackupPath { get { return Path.Combine(AppSettings.Dir, "proxy-backup.json"); } }

        public static bool IsEngaged { get { return File.Exists(BackupPath); } }

        public static void Engage(int port, string bypass)
        {
            lock (sync)
            {
                string server = "127.0.0.1:" + port;
                if (!File.Exists(BackupPath))
                {
                    var current = Query();
                    if (IsOurs(current.Bypass)) current = DefaultSettings();
                    File.WriteAllText(BackupPath, new JavaScriptSerializer().Serialize(current));
                    SetRunOnce(true);
                    lastApplied = null;
                }
                string key = server + "|" + bypass;
                if (key == lastApplied) return;
                if (Apply(PROXY_TYPE_DIRECT | PROXY_TYPE_PROXY, server, bypass, null)) lastApplied = key;
            }
        }

        public static void Disengage()
        {
            lock (sync)
            {
                lastApplied = null;
                if (!File.Exists(BackupPath)) return;
                ProxyBackup b = null;
                try { b = new JavaScriptSerializer().Deserialize<ProxyBackup>(File.ReadAllText(BackupPath)); } catch { }
                if (b == null || (IsOurs(b.Bypass))) b = DefaultSettings();
                if (b.Flags == 0) b.Flags = PROXY_TYPE_DIRECT;
                if (Apply(b.Flags, b.Server, b.Bypass, b.AutoConfigUrl))
                {
                    try { File.Delete(BackupPath); } catch { }
                    SetRunOnce(false);
                }
            }
        }

        // Startup / emergency: undo anything Fokkus may have left behind (crash, power loss...).
        public static void RecoverAll()
        {
            try
            {
                if (IsEngaged) { Disengage(); return; }
                var cur = Query();
                if (IsOurs(cur.Bypass))
                {
                    var d = DefaultSettings();
                    Apply(d.Flags, d.Server, d.Bypass, d.AutoConfigUrl);
                }
                SetRunOnce(false);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        static ProxyBackup DefaultSettings()
        {
            var b = new ProxyBackup();
            b.Flags = PROXY_TYPE_DIRECT | PROXY_TYPE_AUTO_DETECT;
            return b;
        }

        static void SetRunOnce(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunOnceKey))
                {
                    if (k == null) return;
                    if (on) k.SetValue(RunOnceName, "\"" + Assembly.GetEntryAssembly().Location + "\" --restore-internet --silent");
                    else if (k.GetValue(RunOnceName) != null) k.DeleteValue(RunOnceName, false);
                    if (k.GetValue(LegacyRunOnceName) != null) k.DeleteValue(LegacyRunOnceName, false);
                }
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        static IntPtr Offset(IntPtr p, int bytes) { return new IntPtr(p.ToInt64() + bytes); }

        static bool Apply(int flags, string server, string bypass, string pac)
        {
            var allocs = new List<IntPtr>();
            int optSize = Marshal.SizeOf(typeof(Option));
            IntPtr opts = Marshal.AllocHGlobal(optSize * 4);
            IntPtr list = IntPtr.Zero;
            try
            {
                int[] ids = { PER_CONN_FLAGS, PER_CONN_PROXY_SERVER, PER_CONN_PROXY_BYPASS, PER_CONN_AUTOCONFIG_URL };
                string[] values = { null, server, bypass, pac };
                for (int i = 0; i < 4; i++)
                {
                    var o = new Option();
                    o.dwOption = ids[i];
                    if (i == 0) o.Value.dwValue = flags;
                    else
                    {
                        IntPtr p = string.IsNullOrEmpty(values[i]) ? IntPtr.Zero : Marshal.StringToHGlobalUni(values[i]);
                        if (p != IntPtr.Zero) allocs.Add(p);
                        o.Value.pszValue = p;
                    }
                    Marshal.StructureToPtr(o, Offset(opts, i * optSize), false);
                }
                var l = new OptionList();
                l.dwSize = Marshal.SizeOf(typeof(OptionList));
                l.pszConnection = IntPtr.Zero;
                l.dwOptionCount = 4;
                l.pOptions = opts;
                list = Marshal.AllocHGlobal(l.dwSize);
                Marshal.StructureToPtr(l, list, false);
                bool ok = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_PER_CONNECTION_OPTION, list, l.dwSize);
                if (!ok) Log.Write("InternetSetOption failed: " + Marshal.GetLastWin32Error());
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_PROXY_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
                return ok;
            }
            finally
            {
                foreach (var p in allocs) Marshal.FreeHGlobal(p);
                Marshal.FreeHGlobal(opts);
                if (list != IntPtr.Zero) Marshal.FreeHGlobal(list);
            }
        }

        public static ProxyBackup Query()
        {
            var b = Query(PER_CONN_FLAGS_UI) ?? Query(PER_CONN_FLAGS);
            return b ?? DefaultSettings();
        }

        static ProxyBackup Query(int flagsOption)
        {
            int optSize = Marshal.SizeOf(typeof(Option));
            IntPtr opts = Marshal.AllocHGlobal(optSize * 4);
            IntPtr list = IntPtr.Zero;
            try
            {
                int[] ids = { flagsOption, PER_CONN_PROXY_SERVER, PER_CONN_PROXY_BYPASS, PER_CONN_AUTOCONFIG_URL };
                for (int i = 0; i < 4; i++)
                {
                    var o = new Option();
                    o.dwOption = ids[i];
                    Marshal.StructureToPtr(o, Offset(opts, i * optSize), false);
                }
                var l = new OptionList();
                l.dwSize = Marshal.SizeOf(typeof(OptionList));
                l.dwOptionCount = 4;
                l.pOptions = opts;
                list = Marshal.AllocHGlobal(l.dwSize);
                Marshal.StructureToPtr(l, list, false);
                int size = l.dwSize;
                if (!InternetQueryOption(IntPtr.Zero, INTERNET_OPTION_PER_CONNECTION_OPTION, list, ref size)) return null;
                var r = new ProxyBackup();
                for (int i = 0; i < 4; i++)
                {
                    var o = (Option)Marshal.PtrToStructure(Offset(opts, i * optSize), typeof(Option));
                    if (i == 0) { r.Flags = o.Value.dwValue; continue; }
                    string str = null;
                    if (o.Value.pszValue != IntPtr.Zero)
                    {
                        str = Marshal.PtrToStringUni(o.Value.pszValue);
                        GlobalFree(o.Value.pszValue);
                    }
                    if (i == 1) r.Server = str;
                    else if (i == 2) r.Bypass = str;
                    else r.AutoConfigUrl = str;
                }
                return r;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(opts);
                if (list != IntPtr.Zero) Marshal.FreeHGlobal(list);
            }
        }
    }
}
