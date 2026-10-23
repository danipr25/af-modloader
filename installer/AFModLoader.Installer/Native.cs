using System;
using System.Runtime.InteropServices;

namespace AFModLoader.Installer
{
    /// <summary>The few Windows calls the look of the window needs. Every one of them may fail on an older Windows.</summary>
    internal static class Native
    {
        public const int WM_SYSCOLORCHANGE = 0x0015;
        public const int WM_SETTINGCHANGE = 0x001A;
        public const int SPI_SETHIGHCONTRAST = 0x0043;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>Dark title bar on or off. Ignored when Windows does not know the setting.</summary>
        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            try
            {
                int value = dark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_FRAMECHANGED | SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                // Older Windows: the title bar stays as it is.
            }
        }

        /// <summary>Dark or light scrollbars for a control. Ignored when Windows does not have the theme.</summary>
        public static void SetControlTheme(IntPtr hwnd, bool dark)
        {
            try
            {
                SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                // Older Windows: the scrollbars stay as they are.
            }
        }
    }
}
