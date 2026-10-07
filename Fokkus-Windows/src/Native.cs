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
    }
}
