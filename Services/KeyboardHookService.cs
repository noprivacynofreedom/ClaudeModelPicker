using SharpHook;
using SharpHook.Native;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClaudeModelPicker.Models;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Installs a global low-level keyboard hook and reacts to Enter.
    ///
    /// Only reacts when Claude Desktop (process "claude") is the foreground
    /// app. Every other app's Enter passes straight through untouched.
    /// </summary>
    public class KeyboardHookService
    {
        private readonly SimpleGlobalHook _hook;
        private readonly InputMethodManager _inputManager;
        private readonly PromptAnalyzer _analyzer;
        private readonly ClaudeMonitorService _flaUiFallback;
        private readonly FileLogger? _logger;
        private readonly ConfigManager _config;
        private bool _isRunning;

        private DateTime _lastTrigger = DateTime.MinValue;
        private readonly object _throttleLock = new();

        // 1 while a read/select is in progress. Our own simulated {ENTER}
        // (KeyboardModelSelector) arrives ~1 s after the real one, outside the
        // throttle window, so without this it re-triggers the hook (D4).
        private int _busy;

        public KeyboardHookService(
            InputMethodManager inputManager,
            PromptAnalyzer analyzer,
            ClaudeMonitorService flaUiFallback,
            ConfigManager config,
            FileLogger? logger = null)
        {
            _inputManager = inputManager;
            _analyzer = analyzer;
            _flaUiFallback = flaUiFallback;
            _config = config;
            _logger = logger;
            _hook = new SimpleGlobalHook();
            _hook.KeyPressed += OnKeyPressed;
        }

        public void Start()
        {
            if (_isRunning) return;
            try
            {
                _hook.RunAsync();
                _isRunning = true;
                _logger?.LogEvent("HOOK_STARTED");
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardHookService.Start", ex);
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
                _logger?.LogEvent("HOOK_STOPPED");
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardHookService.Stop", ex);
            }
        }

        /// <summary>
        /// Fires on the global hook's own thread. Does the minimum possible
        /// work here (key filter + throttle check) and offloads everything
        /// else — clipboard/keyboard-nav I/O, analysis, popup — to a
        /// background task, so a slow prompt read or a stuck dialog never
        /// blocks keystrokes system-wide. This was flagged as a P1 item in
        /// HANDOFF-SONNET.md ("Hook fires synchronously ... can freeze
        /// keyboard") and was not fixed in the original version.
        /// </summary>
        private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
        {
            if (e.Data.KeyCode != KeyCode.VcEnter && e.Data.KeyCode != KeyCode.VcNumPadEnter) return;

            // Keystrokes we sent ourselves never trigger another round.
            if (e.IsEventSimulated) return;

            // Safety gate (D2 + D5): nothing below may run unless Claude Desktop
            // is the foreground app, or Ctrl+A / Ctrl+C lands in whatever app is.
            if (!ForegroundCheck.IsClaudeForeground()) return;

            if (Volatile.Read(ref _busy) == 1)
            {
                _logger?.LogEvent("HOOK_BUSY");
                return;
            }

            if (!TryPassThrottle())
            {
                _logger?.LogEvent("HOOK_THROTTLED");
                return;
            }

            if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            {
                _logger?.LogEvent("HOOK_BUSY");
                return;
            }

            _logger?.LogHook(triggered: true);
            Task.Run(() =>
            {
                try { HandleEnterAsync(); }
                finally { Volatile.Write(ref _busy, 0); }
            });
        }

        private bool TryPassThrottle()
        {
            lock (_throttleLock)
            {
                var now = DateTime.UtcNow;
                var throttleMs = _config.ModelSelectionThrottleMs;
                if ((now - _lastTrigger).TotalMilliseconds < throttleMs)
                    return false;

                _lastTrigger = now;
                return true;
            }
        }

        private void HandleEnterAsync()
        {
            try
            {
                var prompt = _inputManager.ReadPrompt();
                if (string.IsNullOrWhiteSpace(prompt))
                {
                    _logger?.LogHook(triggered: true, reason: "empty_prompt");
                    return;
                }

                var pick = _analyzer.Analyze(prompt);
                _logger?.LogAnalysis(PromptAnalyzer.SanitizePrompt(prompt), pick.PickedModel, pick.Confidence, pick.TokenCount);

                var autoAcceptThreshold = _config.PopupAutoAcceptConfidence;
                if (pick.Confidence > autoAcceptThreshold)
                {
                    if (!_inputManager.SelectModel(pick.PickedModel))
                        ShowPopupOnUiThread(pick);
                }
                else
                {
                    ShowPopupOnUiThread(pick);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardHookService.HandleEnterAsync", ex);
            }
        }

        /// <summary>
        /// ModelPickDialog is a WPF Window — it must be shown on the UI
        /// thread, not the background Task this method is called from.
        /// </summary>
        private void ShowPopupOnUiThread(ModelPick pick)
        {
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var dialog = new ModelPickDialog(pick);
                    dialog.ShowDialog();
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardHookService.ShowPopupOnUiThread", ex);
            }
        }
    }
}
