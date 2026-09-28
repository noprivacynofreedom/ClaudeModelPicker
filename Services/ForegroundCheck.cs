using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// One place that answers "is Claude Desktop the foreground app?".
    ///
    /// Matches on the owning process name, not the window title. A title
    /// check ("contains Claude") also matches a claude.ai browser tab, a
    /// Notepad file called claude.txt, and so on. Every synthetic keystroke
    /// this app sends (Ctrl+A, Ctrl+C, Tab, Enter) must be gated by this,
    /// because Ctrl+C into a terminal kills the running process.
    ///
    /// Fails closed: any error returns false.
    /// </summary>
    public static class ForegroundCheck
    {
        private const string ClaudeProcessName = "claude";

        public static bool IsClaudeForeground()
        {
            try
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
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

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
