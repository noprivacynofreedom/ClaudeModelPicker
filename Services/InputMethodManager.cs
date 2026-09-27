using System;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Picks between input methods for reading a prompt and selecting a model:
    /// clipboard/keyboard first (works on Electron), FlaUI as a fallback
    /// (config-gated, off by default — see modelSelection.methods.flaui.enabled).
    /// </summary>
    public class InputMethodManager
    {
        private readonly ClipboardInputReader _clipboardReader;
        private readonly KeyboardModelSelector _keyboardSelector;
        private readonly FlaUIInputService _flaUiFallback;
        private readonly ConfigManager _config;
        private readonly FileLogger? _logger;

        public InputMethodManager(ConfigManager config, FlaUIInputService flaUiFallback, FileLogger? logger = null)
        {
            _config = config;
            _flaUiFallback = flaUiFallback;
            _logger = logger;
            _clipboardReader = new ClipboardInputReader(config.ClipboardRestoreContent);
            _keyboardSelector = new KeyboardModelSelector(config.KeyboardTabCount);
        }

        public string ReadPrompt()
        {
            if (_config.ClipboardEnabled)
            {
                var prompt = _clipboardReader.ReadFocusedFieldText();
                if (!string.IsNullOrEmpty(prompt))
                {
                    _logger?.LogEvent("PROMPT_READ", ("method", "clipboard"));
                    return prompt;
                }
            }

            if (_config.FlaUIEnabled)
            {
                var prompt = _flaUiFallback.ReadClaudeInputField();
                _logger?.LogEvent("PROMPT_READ", ("method", "flaui"), ("empty", string.IsNullOrEmpty(prompt).ToString()));
                return prompt;
            }

            _logger?.LogEvent("PROMPT_READ", ("method", "none"), ("empty", "true"));
            return string.Empty;
        }

        public bool SelectModel(string model)
        {
            if (_config.KeyboardEnabled && _keyboardSelector.SelectModel(model))
            {
                _logger?.LogSelection(model, "keyboard", success: true);
                return true;
            }

            if (_config.FlaUIEnabled && _flaUiFallback.TryClickModelDropdown(model))
            {
                _logger?.LogSelection(model, "flaui", success: true);
                return true;
            }

            _logger?.LogSelection(model, "none", success: false);
            return false;
        }
    }
}
