using System;
using System.Threading;
using System.Windows.Forms;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Reads the current prompt from Claude Desktop's input field via the
    /// clipboard (Ctrl+A, Ctrl+C, read, restore) instead of UI Automation.
    /// See RESEARCH-InputMethods.md for why: Claude Desktop is Electron, and
    /// FlaUI's accessibility-tree walk frequently finds nothing to select on
    /// Electron apps unless the app explicitly exposes ARIA labels.
    /// </summary>
    public class ClipboardInputReader
    {
        private readonly FileLogger? _logger;

        public ClipboardInputReader(FileLogger? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Selects all + copies the focused field, reads the clipboard, and
        /// restores whatever was on the clipboard before. Returns empty
        /// string on any failure — callers should fall back to FlaUI.
        /// </summary>
        public string ReadPromptViaClipboard(bool restoreClipboard = true)
        {
            string? savedClipboard = null;

            try
            {
                if (Clipboard.ContainsText())
                    savedClipboard = SafeGetText();

                SendKeys.SendWait("^a");
                Thread.Sleep(50);
                SendKeys.SendWait("^c");
                Thread.Sleep(100); // clipboard write is async in Electron/Chromium — give it time

                var prompt = SafeGetText() ?? string.Empty;
                _logger?.LogEvent("CLIPBOARD_READ", ("length", prompt.Length.ToString()));
                return prompt;
            }
            catch (Exception ex)
            {
                _logger?.LogError("ClipboardInputReader.ReadPromptViaClipboard", ex);
                return string.Empty;
            }
            finally
            {
                if (restoreClipboard)
                {
                    if (savedClipboard != null)
                        SafeSetText(savedClipboard);
                    else
                        SafeClear();
                }
            }
        }

        private static string? SafeGetText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another process is holding the clipboard lock — common on Windows.
                Thread.Sleep(50);
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { return null; }
            }
        }

        private static void SafeSetText(string text)
        {
            try { Clipboard.SetText(text); } catch { /* best effort */ }
        }

        private static void SafeClear()
        {
            try { Clipboard.Clear(); } catch { /* best effort */ }
        }
    }
}
