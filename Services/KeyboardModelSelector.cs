using System;
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
    /// a password field, etc). ForegroundCheck.IsClaudeForeground() is called first and
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
            if (!ForegroundCheck.IsClaudeForeground())
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

                // Walk focus back to the prompt box so the caller's re-sent
                // Enter sends the message. Mirrors the Tab walk above, so it is
                // exactly as UNVERIFIED as tabCount.
                for (int i = 0; i < _tabCount; i++)
                {
                    SendKeys.SendWait("+{TAB}");
                    Thread.Sleep(100);
                }

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
    }
}
