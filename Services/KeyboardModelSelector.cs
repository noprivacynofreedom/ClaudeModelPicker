using System;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Selects a model in Claude Desktop's model dropdown via keyboard only
    /// (Tab to the selector, arrow keys to move, Enter to confirm). Avoids
    /// FlaUI, which cannot reliably see into Claude Desktop's Electron UI.
    ///
    /// tabCount is unverified against the real app (see RESEARCH-InputMethods.md
    /// "Questions for Sonnet") — it is config-driven so it can be tuned without
    /// a rebuild once someone has tested it against a live Claude Desktop window.
    /// </summary>
    public class KeyboardModelSelector
    {
        private readonly int _tabCount;
        private readonly int _stepDelayMs;

        public KeyboardModelSelector(int tabCount = 5, int stepDelayMs = 100)
        {
            _tabCount = tabCount;
            _stepDelayMs = stepDelayMs;
        }

        /// <summary>
        /// Navigates to and selects the given model by name ("Haiku" or "Sonnet").
        /// Returns false (without throwing) if anything goes wrong, so callers
        /// can fall back to FlaUI or give up cleanly.
        /// </summary>
        public bool SelectModel(string targetModel)
        {
            try
            {
                for (int i = 0; i < _tabCount; i++)
                {
                    SendKeys.SendWait("{TAB}");
                    Thread.Sleep(_stepDelayMs);
                }

                SendKeys.SendWait(" ");
                Thread.Sleep(_stepDelayMs * 2);

                var direction = targetModel.Equals("Sonnet", StringComparison.OrdinalIgnoreCase)
                    ? "{DOWN}"
                    : "{UP}";
                SendKeys.SendWait(direction);
                Thread.Sleep(_stepDelayMs);

                SendKeys.SendWait("{ENTER}");
                Thread.Sleep(_stepDelayMs * 3);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Keyboard model selection failed: {ex.Message}");
                return false;
            }
        }
    }
}
