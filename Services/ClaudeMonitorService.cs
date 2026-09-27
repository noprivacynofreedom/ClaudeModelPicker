using FlaUI.Core;
using FlaUI.UIA3;
using System.Diagnostics;
using System.Windows;
using ClaudeModelPicker.Models;

namespace ClaudeModelPicker.Services
{
    public class ClaudeMonitorService : IDisposable
    {
        private readonly UIA3Automation _automation;
        private readonly PromptAnalyzer _analyzer;
        private List<ModelPickLog> _pickLog = new();

        public ClaudeMonitorService()
        {
            _automation = new UIA3Automation();
            _analyzer = new PromptAnalyzer();
        }

        public string ReadClaudeInputField()
        {
            try
            {
                var claudeWindow = _automation.GetDesktop()
                    .FindFirstByNameAndControlType("Claude", FlaUI.Core.Definitions.ControlType.Window);

                if (claudeWindow == null)
                    return string.Empty;

                // Look for textarea or input field with user's prompt
                var inputFields = claudeWindow.FindAllByControlType(FlaUI.Core.Definitions.ControlType.Edit);
                if (inputFields.Length == 0)
                    inputFields = claudeWindow.FindAllByControlType(FlaUI.Core.Definitions.ControlType.Text);

                if (inputFields.Length == 0)
                    return string.Empty;

                // Last edit field is usually the current prompt
                var lastField = inputFields.Last();
                var text = lastField.AsTextBox()?.Text ?? string.Empty;

                return text;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading input: {ex.Message}");
                return string.Empty;
            }
        }

        public ModelPick AnalyzePrompt(string prompt)
        {
            return _analyzer.Analyze(prompt);
        }

        public bool TryClickModelDropdown(string model)
        {
            try
            {
                var claudeWindow = _automation.GetDesktop()
                    .FindFirstByNameAndControlType("Claude", FlaUI.Core.Definitions.ControlType.Window);

                if (claudeWindow == null)
                    return false;

                // Find model selector dropdown (looks for button with current model name)
                var buttons = claudeWindow.FindAllByControlType(FlaUI.Core.Definitions.ControlType.Button);
                var modelButton = buttons.FirstOrDefault(b =>
                    b.Name?.Contains("Haiku", StringComparison.OrdinalIgnoreCase) == true ||
                    b.Name?.Contains("Sonnet", StringComparison.OrdinalIgnoreCase) == true ||
                    b.Name?.Contains("Model", StringComparison.OrdinalIgnoreCase) == true);

                if (modelButton == null)
                    return false;

                // Click dropdown to open
                modelButton.Click();
                Thread.Sleep(300);

                // Find and click the target model in dropdown
                var dropdownItems = claudeWindow.FindAllByControlType(FlaUI.Core.Definitions.ControlType.MenuItem);
                var targetItem = dropdownItems.FirstOrDefault(item =>
                    item.Name?.Contains(model, StringComparison.OrdinalIgnoreCase) == true);

                if (targetItem == null)
                {
                    // Try clicking dropdown again to close it
                    modelButton.Click();
                    return false;
                }

                targetItem.Click();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clicking dropdown: {ex.Message}");
                return false;
            }
        }

        public void ShowPopup(ModelPick pick)
        {
            try
            {
                var dialog = new ModelPickDialog(pick);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing popup: {ex.Message}");
            }
        }

        public void LogModelPick(string prompt, ModelPick pick)
        {
            _pickLog.Add(new ModelPickLog
            {
                Timestamp = DateTime.Now,
                Prompt = prompt.Substring(0, Math.Min(100, prompt.Length)),
                PickedModel = pick.PickedModel,
                Confidence = pick.Confidence
            });

            if (_pickLog.Count > 1000)
                _pickLog = _pickLog.TakeLast(1000).ToList();
        }

        public List<ModelPickLog> GetPickHistory() => _pickLog;

        public void Dispose()
        {
            _automation?.Dispose();
        }
    }
}
