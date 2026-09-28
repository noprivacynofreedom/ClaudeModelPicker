using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Selects a model in Claude Desktop's model dropdown via keyboard
    /// navigation (Tab to the selector, Enter/Space to open, arrows to pick,
    /// Enter to confirm) instead of FlaUI clicking.
    ///
    /// SECURITY (P1, see SECURITY-AUDIT.md "Keyboard nav focus issue"):
    /// blindly sending Tab/Arrow/Enter keystrokes is dangerous if some other
    /// window stole focus between the Enter keypress and this call running —
    /// it would type into whatever the user is actually looking at (email,
    /// a password field, etc). VerifyClaudeIsForeground() is called first and
    /// this refuses to send anything if it fails.
    /// </summary>
    public class KeyboardModelSelector
    {
        private readonly FileLogger? _logger;
        private readonly int _tabCount;

        public KeyboardModelSelector(FileLogger? logger = null, int tabCount = 5)
        {
            _logger = logger;
            _tabCount = tabCount;
        }

        public bool SelectModelViaKeyboard(string targetModel)
        {
            if (!VerifyClaudeIsForeground())
            {
                _logger?.LogEvent("KEYBOARD_SELECT_ABORTED", ("reason", "claude_not_foreground"));
                return false;
            }

            try
            {
                for (int i = 0; i < _tabCount; i++)
                {
                    SendKeys.SendWait("{TAB}");
                    Thread.Sleep(100);
                }

                SendKeys.SendWait(" "); // open dropdown
                Thread.Sleep(200);

                if (targetModel.Equals("Sonnet", StringComparison.OrdinalIgnoreCase))
                    SendKeys.SendWait("{DOWN}");
                else if (targetModel.Equals("Haiku", StringComparison.OrdinalIgnoreCase))
                    SendKeys.SendWait("{UP}");

                Thread.Sleep(100);
                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(300);

                _logger?.LogEvent("KEYBOARD_SELECT", ("model", targetModel), ("result", "sent"));
                // No UI-Automation feedback loop exists to confirm the click landed —
                // this returns true optimistically. Tab count (_tabCount, from
                // config.json modelSelection.methods.keyboard.tabCount) and dropdown
                // order are UNVERIFIED against real Claude Desktop; see HANDOFF-OPUS.md.
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardModelSelector.SelectModelViaKeyboard", ex);
                return false;
            }
        }

        /// <summary>
        /// Checks the foreground window's title contains "Claude" before
        /// this class sends any synthetic keystrokes. Cheap P/Invoke check —
        /// no new NuGet dependency needed for something this simple.
        /// </summary>
        private bool VerifyClaudeIsForeground()
        {
            try
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero)
                    return false;

                var builder = new StringBuilder(256);
                GetWindowText(handle, builder, builder.Capacity);
                var title = builder.ToString();

                return title.Contains("Claude", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardModelSelector.VerifyClaudeIsForeground", ex);
                return false; // fail closed — refuse to send keys if we can't verify
            }
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    }
}
