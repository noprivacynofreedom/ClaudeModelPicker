using System;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Single entry point for "read the prompt" and "pick the model".
    /// Reading: clipboard first (works on Electron), FlaUI as fallback.
    /// Picking: FlaUI UI Automation only (ExpandCollapse + Invoke). The old
    /// Tab-walk keyboard selector was removed: on the real app it tabbed
    /// across the UI instead of opening the model menu.
    /// Method choice is logged so the log shows which path actually fired.
    /// </summary>
    public class InputMethodManager
    {
        private readonly ClipboardInputReader _clipboardReader;
        private readonly ClaudeMonitorService _flaUi;
        private readonly ConfigManager _config;
        private readonly FileLogger? _logger;

        public InputMethodManager(
            ClaudeMonitorService flaUi,
            ConfigManager config,
            FileLogger? logger = null)
        {
            _flaUi = flaUi;
            _config = config;
            _logger = logger;
            _clipboardReader = new ClipboardInputReader(logger);
        }

        public string ReadPrompt()
        {
            if (_config.ClipboardEnabled)
            {
                var prompt = _clipboardReader.ReadPromptViaClipboard(_config.ClipboardRestoreContent);
                if (!string.IsNullOrEmpty(prompt))
                {
                    _logger?.LogSelection("n/a", "clipboard-read", true);
                    return prompt;
                }
            }

            if (_config.FlaUIEnabled)
            {
                var prompt = _flaUi.ReadClaudeInputFieldViaFlaUI();
                _logger?.LogSelection("n/a", "flaui-read", !string.IsNullOrEmpty(prompt));
                return prompt;
            }

            return string.Empty;
        }

        public bool SelectModel(string model)
        {
            if (_config.FlaUIEnabled)
            {
                var ok = _flaUi.TryClickModelDropdownViaFlaUI(model);
                _logger?.LogSelection(model, "flaui-click", ok);
                if (ok) return true;
            }

            _logger?.LogSelection(model, "none-succeeded", false);
            return false;
        }
    }
}
