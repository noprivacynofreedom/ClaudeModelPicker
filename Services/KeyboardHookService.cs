using SharpHook;
using SharpHook.Native;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Forms = System.Windows.Forms;
using ClaudeModelPicker.Models;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Installs a global low-level keyboard hook and reacts to Enter.
    ///
    /// Only reacts to a plain Enter when Claude Desktop (process "claude") is
    /// the foreground app. Every other Enter passes straight through.
    /// A handled Enter is suppressed, the prompt is read and analysed on an
    /// STA thread, and then the Enter is re-sent so the message still goes.
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

        // 1 when a suppressed Enter could not be re-sent (Claude lost focus,
        // e.g. to the popup). The user's next Enter in Claude then passes
        // straight through instead of starting another read.
        private int _passNextEnter;

        private const int FocusSettleMs = 250;

        /// <summary>
        /// Raised (on the hook thread) each time a real Enter in Claude Desktop
        /// is accepted for handling. App uses it to reset the idle shutdown timer.
        /// </summary>
        public event Action? PromptActivity;

        // Last model the user accepted, picked or skipped. Only touched on the worker thread.
        private string? _lastModel;

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

            // Shift+Enter is a newline in Claude. Ctrl/Alt/Win+Enter are not ours either.
            const ModifierMask anyModifier = ModifierMask.Shift | ModifierMask.Ctrl | ModifierMask.Alt | ModifierMask.Meta;
            if ((e.RawEvent.Mask & anyModifier) != 0) return;

            // Safety gate (D2 + D5): nothing below may run unless Claude Desktop
            // is the foreground app, or Ctrl+A / Ctrl+C lands in whatever app is.
            if (!ForegroundCheck.IsClaudeForeground()) return;

            // Idle-shutdown clock: any real Enter in Claude counts as activity.
            PromptActivity?.Invoke();

            if (Interlocked.Exchange(ref _passNextEnter, 0) == 1)
            {
                _logger?.LogEvent("HOOK_PASS_THROUGH");
                return;
            }

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

            // D1: hold the Enter back so Claude does not send (and clear the
            // box) before we read it. HandleEnter re-sends it when done.
            // Suppression must be set here, synchronously, on the hook thread.
            e.SuppressEvent = true;
            _logger?.LogHook(triggered: true);

            // D3: WinForms Clipboard needs an STA thread. A thread-pool (MTA)
            // thread makes Clipboard.GetText silently return "".
            var worker = new Thread(() =>
            {
                try { HandleEnter(); }
                finally { Volatile.Write(ref _busy, 0); }
            });
            worker.SetApartmentState(ApartmentState.STA);
            worker.IsBackground = true;
            worker.Start();
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

        /// <summary>
        /// Runs on a dedicated STA thread with the user's Enter suppressed.
        /// Always ends by re-sending that Enter, so a failed read or a crash
        /// never swallows the user's message.
        /// </summary>
        private void HandleEnter()
        {
            try
            {
                HandlePrompt();
            }
            finally
            {
                ResendEnter();
            }
        }

        private void HandlePrompt()
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

                // Only ask when the recommendation changes. Same model as last
                // time = no popup, the message just sends.
                if (string.Equals(pick.PickedModel, _lastModel, StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.LogEvent("POPUP_SKIPPED", ("reason", "same_model"), ("model", pick.PickedModel));
                    return;
                }

                var autoAcceptThreshold = _config.PopupAutoAcceptConfidence;
                if (pick.Confidence > autoAcceptThreshold && _inputManager.SelectModel(pick.PickedModel))
                {
                    _lastModel = pick.PickedModel;
                    return;
                }

                var chosen = ShowPopupOnUiThread(pick);
                if (chosen != null)
                {
                    _inputManager.SelectModel(chosen);
                    _logger?.LogEvent("MODEL_CHOSEN", ("model", chosen));
                }

                // Skip counts as "fine, stop asking" for this recommendation.
                _lastModel = chosen ?? pick.PickedModel;
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardHookService.HandlePrompt", ex);
            }
        }

        /// <summary>
        /// Sends the held-back Enter to Claude. {RIGHT} first collapses the
        /// Ctrl+A selection so the Enter cannot replace the prompt text.
        /// If Claude is no longer foreground, sends nothing (fail closed) and
        /// lets the user's next Enter pass through untouched.
        /// </summary>
        private void ResendEnter()
        {
            try
            {
                if (!ForegroundCheck.IsClaudeForeground())
                {
                    Volatile.Write(ref _passNextEnter, 1);
                    _logger?.LogEvent("ENTER_NOT_RESENT", ("reason", "claude_not_foreground"));
                    return;
                }

                Forms.SendKeys.SendWait("{RIGHT}");
                Thread.Sleep(30);
                Forms.SendKeys.SendWait("{ENTER}");
                _logger?.LogEvent("ENTER_RESENT");
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _passNextEnter, 1);
                _logger?.LogError("KeyboardHookService.ResendEnter", ex);
            }
        }

        /// <summary>
        /// After the popup closes, Windows hands the foreground back to
        /// Claude, but Chromium restores focus to the text box a moment
        /// later. An Enter sent in that gap is lost (seen on the real test:
        /// ENTER_RESENT logged, message not sent). Wait for Claude to be
        /// foreground, then give the text box time to take focus.
        /// </summary>
        private void WaitForClaudeFocusAfterPopup()
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < 1000 && !ForegroundCheck.IsClaudeForeground())
                Thread.Sleep(20);

            Thread.Sleep(FocusSettleMs);
            _logger?.LogEvent("POPUP_FOCUS_WAIT", ("ms", timer.ElapsedMilliseconds.ToString()));
        }

        /// <summary>
        /// ModelPickDialog is a WPF Window — it must be shown on the UI
        /// thread, not the background Task this method is called from.
        /// </summary>
        /// <returns>"Haiku" or "Sonnet" if a button was clicked, null for Skip or close.</returns>
        private string? ShowPopupOnUiThread(ModelPick pick)
        {
            string? chosen = null;
            try
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var dialog = new ModelPickDialog(pick);
                    dialog.ShowDialog();
                    chosen = dialog.GetSelectedModel();
                });

                WaitForClaudeFocusAfterPopup();
            }
            catch (Exception ex)
            {
                _logger?.LogError("KeyboardHookService.ShowPopupOnUiThread", ex);
            }
            return chosen;
        }
    }
}
