using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace WSJTX_Controller
{
    class WindowUtils
    {
        private static string appName;
        private static string dlgName;
        private static bool isVisible;
        private static bool result;

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        static extern int GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImportAttribute("User32.dll")]
        private static extern int FindWindow(String ClassName, String WindowName);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindowVisible(IntPtr hWnd);

        static string GetWindowCaption(IntPtr hWnd)
        {
            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            return sb.ToString();
        }

        static bool MyEnumWindowsProc(IntPtr hWnd, IntPtr lParam)
        {
            int pid;

            GetWindowThreadProcessId(hWnd, out pid);

            string caption = GetWindowCaption(hWnd);
            if (IsWindowVisible(hWnd) == isVisible && caption.Contains(appName) && caption.Contains(dlgName))
            {
                result = true;
            }

            return true;
        }

        public static bool DetectWindow(string name, string dlg, bool visible)
        {
            appName = name;
            dlgName = dlg;
            isVisible = visible;
            result = false;
            EnumWindows(MyEnumWindowsProc, IntPtr.Zero);
            return result;
        }
    }
}
