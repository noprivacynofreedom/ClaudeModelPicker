using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// One place that answers "is Claude Desktop the foreground app?".
    ///
    /// Matches on the owning process name AND the Electron window class.
    /// A title check ("contains Claude") also matches a claude.ai browser tab,
    /// a Notepad file called claude.txt, and so on. The process name alone is
    /// not enough either: the Claude Code CLI is also claude.exe, and a console
    /// window running it would pass. Every synthetic keystroke this app sends
    /// (Ctrl+A, Ctrl+C, Enter) must be gated by this, because Ctrl+C into a
    /// terminal kills the running process.
    ///
    /// Fails closed: any error returns false.
    /// </summary>
    public static class ForegroundCheck
    {
        private const string ClaudeProcessName = "claude";
        private const string ElectronWindowClass = "Chrome_WidgetWin_1";

        public static bool IsClaudeForeground()
        {
            try
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return false;

                if (!IsElectronWindowClass(GetClassNameOf(handle)))
                    return false;

                GetWindowThreadProcessId(handle, out uint pid);
                if (pid == 0)
                    return false;

                using var process = Process.GetProcessById((int)pid);
                return string.Equals(process.ProcessName, ClaudeProcessName, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Claude Desktop's main window class. Console hosts use ConsoleWindowClass / CASCADIA_HOSTING_WINDOW_CLASS.</summary>
        public static bool IsElectronWindowClass(string? className) =>
            string.Equals(className, ElectronWindowClass, StringComparison.Ordinal);

        private static string GetClassNameOf(IntPtr handle)
        {
            var sb = new StringBuilder(256);
            return GetClassName(handle, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    }
}
