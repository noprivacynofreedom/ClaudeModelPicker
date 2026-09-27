using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core;
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

        public string ReadClaudeInputField()
        {
            try
            {
                var claudeWindow = _automation.GetDesktop()
                    .FindFirstByNameAndControlType("Claude", ControlType.Window);
                if (claudeWindow == null)
                    return string.Empty;

                var inputFields = claudeWindow.FindAllByControlType(ControlType.Edit);
                if (inputFields.Length == 0)
                    inputFields = claudeWindow.FindAllByControlType(ControlType.Text);
                if (inputFields.Length == 0)
                    return string.Empty;

                var lastField = inputFields.Last();
                return lastField.AsTextBox()?.Text ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FlaUI read failed: {ex.Message}");
                return string.Empty;
            }
        }

        public bool TryClickModelDropdown(string model)
        {
            try
            {
                var claudeWindow = _automation.GetDesktop()
                    .FindFirstByNameAndControlType("Claude", ControlType.Window);
                if (claudeWindow == null)
                    return false;

                var buttons = claudeWindow.FindAllByControlType(ControlType.Button);
                var modelButton = buttons.FirstOrDefault(b =>
                    b.Name?.Contains("Haiku", StringComparison.OrdinalIgnoreCase) == true ||
                    b.Name?.Contains("Sonnet", StringComparison.OrdinalIgnoreCase) == true ||
                    b.Name?.Contains("Model", StringComparison.OrdinalIgnoreCase) == true);
                if (modelButton == null)
                    return false;

                modelButton.Click();
                Thread.Sleep(300);

                var dropdownItems = claudeWindow.FindAllByControlType(ControlType.MenuItem);
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
                Debug.WriteLine($"FlaUI dropdown click failed: {ex.Message}");
                return false;
            }
        }

        public void Dispose() => _automation.Dispose();
    }
}
