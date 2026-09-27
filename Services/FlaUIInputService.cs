using System;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// UI-Automation-based reading/selection, kept as a fallback for apps
    /// FlaUI can actually see into. Known not to work reliably against
    /// Claude Desktop's Electron UI — that's why InputMethodManager tries
    /// clipboard/keyboard first and only falls back here when
    /// modelSelection.methods.flaui.enabled is true in config.json.
    /// </summary>
    public class FlaUIInputService : IDisposable
    {
        private readonly UIA3Automation _automation = new();

        private AutomationElement? FindClaudeWindow()
        {
            return _automation.GetDesktop()
                .FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(w => w.Name == "Claude");
        }

        public string ReadClaudeInputField()
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
                System.Diagnostics.Debug.WriteLine($"FlaUI read failed: {ex.Message}");
                return string.Empty;
            }
        }

        public bool TryClickModelDropdown(string model)
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
                    modelButton.Click(); // close the dropdown we opened
                    return false;
                }

                targetItem.Click();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FlaUI dropdown click failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose() => _automation.Dispose();
    }
}
