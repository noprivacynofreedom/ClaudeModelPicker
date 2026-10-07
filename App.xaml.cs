using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ClaudeModelPicker.Services;
using Forms = System.Windows.Forms;

namespace ClaudeModelPicker
{
    public partial class App : Application
    {
        // Exit codes for "--select <model>" (read by ClaudeUsageMonitor).
        public const int SelectOk = 0;
        public const int SelectFailed = 1;
        public const int SelectBadArgs = 2;

        private ConfigManager? _configManager;
        private FileLogger? _fileLogger;
        private ClaudeMonitorService? _flaUiFallback;
        private KeyboardHookService? _hookService;
        private ModeStore? _modes;
        private Forms.NotifyIcon? _trayIcon;
        private Forms.ToolStripMenuItem? _activeItem;
        private Forms.ToolStripMenuItem? _passiveItem;

        // One copy only: "cmp online" run twice must not start two hooks.
        private Mutex? _singleInstance;

        // Idle shutdown: last real Enter in Claude (UTC ticks). Checked every 30 s.
        private long _lastActivityTicks;
        private DispatcherTimer? _idleTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Diagnostic mode: "ClaudeModelPicker.exe --uia-dump" writes Claude's UI
            // element names to a file, then exits. No hook, no tray, no mutex.
            if (e.Args.Contains("--uia-dump"))
            {
                var dumpPath = UiaDiagnostic.Run();
                MessageBox.Show("UIA dump written to:\n" + dumpPath, "ClaudeModelPicker");
                Shutdown();
                return;
            }

            // One-shot switch: "ClaudeModelPicker.exe --select Opus" switches Claude
            // Desktop's model and exits with SelectOk / SelectFailed / SelectBadArgs.
            // ClaudeUsageMonitor's model drop-down runs this. No hook, no tray, no mutex.
            var selectAt = Array.IndexOf(e.Args, "--select");
            if (selectAt >= 0)
            {
                Shutdown(RunSelect(e.Args.Skip(selectAt + 1).FirstOrDefault()));
                return;
            }

            _singleInstance = new Mutex(true, "ClaudeModelPicker.SingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                Shutdown();
                return;
            }

            // No StartupUri and no main window: this is a tray-only app.
            // ShutdownMode=OnExplicitShutdown keeps it alive until tray Exit.

            try
            {
                _fileLogger = new FileLogger();
                _configManager = new ConfigManager();
                _modes = new ModeStore();

                _fileLogger.CleanupOldLogs(_configManager.AppLogsToKeepDays);

                _flaUiFallback = new ClaudeMonitorService(_fileLogger);
                var inputManager = new InputMethodManager(_flaUiFallback, _configManager, _fileLogger);
                var analyzer = new PromptAnalyzer(_configManager);

                _hookService = new KeyboardHookService(inputManager, analyzer, _flaUiFallback, _configManager, _fileLogger, _modes);
                _hookService.Start();

                _lastActivityTicks = DateTime.UtcNow.Ticks;
                _hookService.PromptActivity += () =>
                    Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

                _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _idleTimer.Tick += (_, _) => CheckIdle();
                _idleTimer.Start();

                if (_configManager.AppMinimizeToTray)
                    SetupTrayIcon();

                _modes.Changed += mode =>
                {
                    _fileLogger?.LogEvent("MODE_CHANGED", ("mode", ModeStore.Format(mode)));
                    Dispatcher.BeginInvoke(new Action(UpdateTrayMode));
                };

                _fileLogger.LogEvent("APP_STARTED", ("mode", ModeStore.Format(_modes.Mode)));
            }
            catch (Exception ex)
            {
                _fileLogger?.LogError("App.OnStartup", ex);
                MessageBox.Show($"Failed to start hook service: {ex.Message}", "Error");
                // No window and OnExplicitShutdown: without this the process would stay alive invisibly.
                Shutdown();
            }
        }

        private static int RunSelect(string? requested)
        {
            var model = ModelNames.Families.FirstOrDefault(f =>
                string.Equals(f, requested, StringComparison.OrdinalIgnoreCase));
            if (model == null) return SelectBadArgs;

            using var logger = new FileLogger();
            using var flaUi = new ClaudeMonitorService(logger);
            var ok = flaUi.TryClickModelDropdownViaFlaUI(model);
            logger.LogSelection(model, "manual-select", ok);
            return ok ? SelectOk : SelectFailed;
        }

        /// <summary>
        /// Tray icon: Active / Passive mode and Exit. Passive lets every Enter
        /// through untouched; the same switch is in ClaudeUsageMonitor's window.
        /// </summary>
        private void SetupTrayIcon()
        {
            try
            {
                _trayIcon = new Forms.NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Application,
                    Visible = true
                };

                var menu = new Forms.ContextMenuStrip();
                _activeItem = new Forms.ToolStripMenuItem("Active: pick a model on Enter");
                _activeItem.Click += (_, _) => _modes?.Set(PickerMode.Active);
                _passiveItem = new Forms.ToolStripMenuItem("Passive: Enter sends as normal");
                _passiveItem.Click += (_, _) => _modes?.Set(PickerMode.Passive);
                menu.Items.Add(_activeItem);
                menu.Items.Add(_passiveItem);
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("Exit", null, (_, _) => Shutdown());
                _trayIcon.ContextMenuStrip = menu;
                UpdateTrayMode();
            }
            catch (Exception ex)
            {
                // Tray icon is a convenience, not a dependency — app keeps running headless if this fails.
                _fileLogger?.LogError("App.SetupTrayIcon", ex);
            }
        }

        private void UpdateTrayMode()
        {
            var passive = _modes?.Mode == PickerMode.Passive;
            if (_activeItem != null) _activeItem.Checked = !passive;
            if (_passiveItem != null) _passiveItem.Checked = passive;
            if (_trayIcon != null) _trayIcon.Text = passive ? "Claude Model Picker (passive)" : "Claude Model Picker (active)";
        }

        private void CheckIdle()
        {
            var limitMinutes = _configManager?.AppIdleShutdownMinutes ?? 10;
            var last = new DateTime(Interlocked.Read(ref _lastActivityTicks), DateTimeKind.Utc);
            var idle = DateTime.UtcNow - last;
            if (idle.TotalMinutes < limitMinutes) return;

            _fileLogger?.LogEvent("IDLE_SHUTDOWN", ("idle_minutes", ((int)idle.TotalMinutes).ToString()));
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _idleTimer?.Stop();
            if (_hookService != null) _fileLogger?.LogEvent("APP_EXITING");
            _hookService?.Stop();
            _flaUiFallback?.Dispose();
            _configManager?.Dispose();
            _modes?.Dispose();
            _trayIcon?.Dispose();
            _fileLogger?.Dispose();
            base.OnExit(e);
        }
    }
}
