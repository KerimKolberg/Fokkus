using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Fokkus
{
    static class Native
    {
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        const int EM_SETCUEBANNER = 0x1501;

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        public static void DarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }

        // Windows 11 rounded corners for borderless windows (ignored on Windows 10).
        public static void RoundCorners(IntPtr hwnd)
        {
            try { int pref = 2; DwmSetWindowAttribute(hwnd, 33, ref pref, 4); } catch { }
        }

        public static void BorderColor(IntPtr hwnd, Color c)
        {
            try { int colorRef = c.R | (c.G << 8) | (c.B << 16); DwmSetWindowAttribute(hwnd, 34, ref colorRef, 4); } catch { }
        }

        public static void DarkScrollbars(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
        }

        // Dark common-file-dialog theme: gives combo boxes a dark frame and drop-down button.
        public static void DarkControl(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, "DarkMode_CFD", null); } catch { }
        }

        public static void SetCueBanner(IntPtr hwnd, string text)
        {
            try { SendMessage(hwnd, EM_SETCUEBANNER, (IntPtr)1, text); } catch { }
        }

        // ---- keeping an always-on-top window on top

        [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hWnd);

        const int GWL_EXSTYLE = -20;
        const int SW_SHOWNOACTIVATE = 4;
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public static bool IsTopMost(IntPtr hwnd) { return (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0; }

        public static void MakeTopMost(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
        }

        public static bool IsMinimized(IntPtr hwnd) { return IsIconic(hwnd); }

        // Restores a minimized window without taking the keyboard focus.
        public static void RestoreNoActivate(IntPtr hwnd) { ShowWindow(hwnd, SW_SHOWNOACTIVATE); }

        // ---- virtual desktops (Windows 10+)

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
        interface IVirtualDesktopManager
        {
            [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow, [MarshalAs(UnmanagedType.Bool)] out bool onCurrentDesktop);
            [PreserveSig] int GetWindowDesktopId(IntPtr topLevelWindow, out Guid desktopId);
            [PreserveSig] int MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
        }

        [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
        class VirtualDesktopManagerClass { }

        static IVirtualDesktopManager desktops;
        static bool desktopsFailed;

        // False only when we know for sure the window sits on another virtual desktop.
        public static bool OnCurrentDesktop(IntPtr hwnd)
        {
            if (desktopsFailed) return true;
            try
            {
                if (desktops == null) desktops = (IVirtualDesktopManager)new VirtualDesktopManagerClass();
                bool on;
                return desktops.IsWindowOnCurrentVirtualDesktop(hwnd, out on) != 0 || on;
            }
            catch { desktopsFailed = true; return true; }
        }
    }
}
