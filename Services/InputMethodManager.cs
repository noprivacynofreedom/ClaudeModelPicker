using System;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Single entry point for "read the prompt" and "pick the model" — tries
    /// the clipboard/keyboard-nav path first (works on Electron), falls back
    /// to FlaUI UI Automation (legacy, likely broken on Electron — see
    /// ClaudeMonitorService's doc comment) if that's disabled or fails.
    /// Method choice for both reading and selecting is logged so a returning
    /// developer can tell which path actually fired in practice.
    /// </summary>
    public class InputMethodManager
    {
        private readonly ClipboardInputReader _clipboardReader;
        private readonly KeyboardModelSelector _keyboardSelector;
        private readonly ClaudeMonitorService _flaUiFallback;
        private readonly ConfigManager _config;
        private readonly FileLogger? _logger;

        public InputMethodManager(
            ClaudeMonitorService flaUiFallback,
            ConfigManager config,
            FileLogger? logger = null)
        {
            _flaUiFallback = flaUiFallback;
            _config = config;
            _logger = logger;
            _clipboardReader = new ClipboardInputReader(logger);
            _keyboardSelector = new KeyboardModelSelector(logger, config.KeyboardTabCount);
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
                var prompt = _flaUiFallback.ReadClaudeInputFieldViaFlaUI();
                _logger?.LogSelection("n/a", "flaui-read", !string.IsNullOrEmpty(prompt));
                return prompt;
            }

            return string.Empty;
        }

        public bool SelectModel(string model)
        {
            if (_config.KeyboardEnabled)
            {
                if (_keyboardSelector.SelectModelViaKeyboard(model))
                {
                    _logger?.LogSelection(model, "keyboard-nav", true);
                    return true;
                }
            }

            if (_config.FlaUIEnabled)
            {
                var ok = _flaUiFallback.TryClickModelDropdownViaFlaUI(model);
                _logger?.LogSelection(model, "flaui-click", ok);
                if (ok) return true;
            }

            _logger?.LogSelection(model, "none-succeeded", false);
            return false;
        }
    }
}
