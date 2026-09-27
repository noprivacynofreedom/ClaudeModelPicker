using System;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Reads the focused input field's text via clipboard (Ctrl+A, Ctrl+C).
    /// Works on Electron apps (e.g. Claude Desktop) where UI Automation
    /// (FlaUI) cannot see into the DOM. Saves and restores the clipboard
    /// so this has no visible side effect for the user.
    /// </summary>
    public class ClipboardInputReader
    {
        private readonly bool _restoreClipboard;

        public ClipboardInputReader(bool restoreClipboard = true)
        {
            _restoreClipboard = restoreClipboard;
        }

        /// <summary>
        /// Selects all text in whatever control currently has focus, copies
        /// it, and returns it. Returns empty string on any failure so
        /// callers can fall back to another method.
        /// </summary>
        public string ReadFocusedFieldText()
        {
            string? savedClipboard = null;
            try
            {
                if (Clipboard.ContainsText())
                    savedClipboard = Clipboard.GetText();

                SendKeys.SendWait("^a");
                Thread.Sleep(50);
                SendKeys.SendWait("^c");
                Thread.Sleep(100);

                var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                return text ?? string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Clipboard read failed: {ex.Message}");
                return string.Empty;
            }
            finally
            {
                if (_restoreClipboard)
                {
                    try
                    {
                        if (savedClipboard != null)
                            Clipboard.SetText(savedClipboard);
                        else
                            Clipboard.Clear();
                    }
                    catch (Exception ex)
                    {
                        // Clipboard can be locked by another process momentarily; not fatal.
                        System.Diagnostics.Debug.WriteLine($"Clipboard restore failed: {ex.Message}");
                    }
                }
            }
        }
    }
}
