using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Linq;
using System.Threading;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Legacy FlaUI (UI Automation) reader/clicker for Claude Desktop.
    ///
    /// Kept as a fallback behind <see cref="InputMethodManager"/>, not as the
    /// primary path. Claude Desktop is Electron, and Electron apps commonly
    /// expose a thin accessibility tree — a generic "Document"/"Pane" node
    /// rather than named Edit/Button controls — unless the app's developers
    /// added ARIA labels. Whether these selectors actually find anything on
    /// the real app is UNVERIFIED (needs a Windows machine with Claude
    /// Desktop running; see HANDOFF-OPUS.md). The clipboard/keyboard-nav
    /// path in InputMethodManager is the one expected to actually work.
    /// </summary>
    public class ClaudeMonitorService : IDisposable
    {
        private readonly UIA3Automation _automation;
        private readonly FileLogger? _logger;

        // Same API calls as FlaUIInputService on sonnet-build-fixes (34165b9),
        // which compiled against FlaUI 4.0.0 on Jason's PC.
        private AutomationElement? FindClaudeWindow()
        {
            return _automation.GetDesktop()
                .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(w => w.Name == "Claude");
        }

        public ClaudeMonitorService(FileLogger? logger = null)
        {
            _automation = new UIA3Automation();
            _logger = logger;
        }

        /// <summary>
        /// Attempts to read the current prompt text via UI Automation.
        /// Returns empty string if the Claude window, or any input control
        /// inside it, cannot be found.
        /// </summary>
        public string ReadClaudeInputFieldViaFlaUI()
        {
            try
            {
                var claudeWindow = FindClaudeWindow();

                if (claudeWindow == null)
                    return string.Empty;

                var inputFields = claudeWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
                if (inputFields.Length == 0)
                    inputFields = claudeWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));

                if (inputFields.Length == 0)
                    return string.Empty;

                var lastField = inputFields.Last();
                return lastField.AsTextBox()?.Text ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.LogError("ClaudeMonitorService.ReadClaudeInputFieldViaFlaUI", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Attempts to click the given model in Claude Desktop's model
        /// selector dropdown via UI Automation. Returns false if the button
        /// or the target menu item cannot be found.
        /// </summary>
        public bool TryClickModelDropdownViaFlaUI(string model)
        {
            try
            {
                var claudeWindow = FindClaudeWindow();

                if (claudeWindow == null)
                    return false;

                var buttons = claudeWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
                var modelButton = buttons.FirstOrDefault(b =>
                    b.Name?.Contains("Haiku", StringComparison.OrdinalIgnoreCase) == true ||
                    b.Name?.Contains("Sonnet", StringComparison.OrdinalIgnoreCase) == true ||
                    b.Name?.Contains("Model", StringComparison.OrdinalIgnoreCase) == true);

                if (modelButton == null)
                    return false;

                modelButton.Click();
                Thread.Sleep(300);

                var dropdownItems = claudeWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
                var targetItem = dropdownItems.FirstOrDefault(item =>
                    item.Name?.Contains(model, StringComparison.OrdinalIgnoreCase) == true);

                if (targetItem == null)
                {
                    modelButton.Click(); // best-effort: close the dropdown we opened
                    return false;
                }

                targetItem.Click();
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError("ClaudeMonitorService.TryClickModelDropdownViaFlaUI", ex);
                return false;
            }
        }

        public void Dispose()
        {
            _automation?.Dispose();
        }
    }
}
