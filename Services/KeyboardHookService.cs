using System;
using SharpHook;
using SharpHook.Native;

namespace ClaudeModelPicker.Services
{
    public class KeyboardHookService
    {
        private readonly SimpleGlobalHook _hook;
        private readonly ClaudeMonitorService _monitor;
        private readonly ConfigManager _config;
        private bool _isRunning;
        private bool _shiftDown;

        public KeyboardHookService(ClaudeMonitorService monitor, ConfigManager? config = null)
        {
            _monitor = monitor;
            _config = config ?? new ConfigManager();
            _hook = new SimpleGlobalHook();
            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;
        }

        public void Start()
        {
            if (_isRunning) return;
            try
            {
                _hook.RunAsync();
                _isRunning = true;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to start keyboard hook: {ex.Message}", ex);
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;
            try
            {
                _hook.Dispose();
                _isRunning = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error stopping hook: {ex.Message}");
            }
        }

        private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == KeyCode.VcLeftShift || e.Data.KeyCode == KeyCode.VcRightShift)
                _shiftDown = false;
        }

        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode == KeyCode.VcLeftShift || e.Data.KeyCode == KeyCode.VcRightShift)
            {
                _shiftDown = true;
                return;
            }

            if (!_config.KeyboardGlobalHookEnabled) return;

            var isEnter = e.Data.KeyCode == KeyCode.VcEnter || e.Data.KeyCode == KeyCode.VcNumPadEnter;
            if (!isEnter) return;

            var shouldIntercept = _shiftDown
                ? _config.KeyboardInterceptShiftEnter
                : _config.KeyboardInterceptEnter;
            if (!shouldIntercept) return;

            try
            {
                // Read Claude input
                var prompt = _monitor.ReadClaudeInputField();
                if (string.IsNullOrWhiteSpace(prompt)) return;

                // Analyze and pick model
                var pick = _monitor.AnalyzePrompt(prompt);

                // Try to auto-click model; fall back to popup if fail
                if (pick.Confidence > _config.PopupAutoAcceptConfidence)
                {
                    if (!_monitor.TryClickModelDropdown(pick.PickedModel))
                    {
                        _monitor.ShowPopup(pick);
                    }
                }
                else
                {
                    _monitor.ShowPopup(pick);
                }

                // Log the pick
                _monitor.LogModelPick(prompt, pick);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hook error: {ex.Message}");
            }
        }
    }
}
