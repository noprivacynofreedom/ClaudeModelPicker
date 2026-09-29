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
        private ConfigManager? _configManager;
        private FileLogger? _fileLogger;
        private ClaudeMonitorService? _flaUiFallback;
        private KeyboardHookService? _hookService;
        private Forms.NotifyIcon? _trayIcon;

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

                _fileLogger.CleanupOldLogs(_configManager.AppLogsToKeepDays);

                _flaUiFallback = new ClaudeMonitorService(_fileLogger);
                var inputManager = new InputMethodManager(_flaUiFallback, _configManager, _fileLogger);
                var analyzer = new PromptAnalyzer(_configManager);

                _hookService = new KeyboardHookService(inputManager, analyzer, _flaUiFallback, _configManager, _fileLogger);
                _hookService.Start();

                _lastActivityTicks = DateTime.UtcNow.Ticks;
                _hookService.PromptActivity += () =>
                    Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

                _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
                _idleTimer.Tick += (_, _) => CheckIdle();
                _idleTimer.Start();

                if (_configManager.AppMinimizeToTray)
                    SetupTrayIcon();

                _fileLogger.LogEvent("APP_STARTED");
            }
            catch (Exception ex)
            {
                _fileLogger?.LogError("App.OnStartup", ex);
                MessageBox.Show($"Failed to start hook service: {ex.Message}", "Error");
                // No window and OnExplicitShutdown: without this the process would stay alive invisibly.
                Shutdown();
            }
        }

        /// <summary>
        /// Minimal system-tray presence: icon + right-click "Exit". The
        /// original HANDOFF-SONNET.md "Missing Features" list asked for a
        /// tray icon; MainWindow previously just hid itself with no tray
        /// presence at all, so there was no way to see the app was running
        /// or to quit it short of Task Manager.
        /// </summary>
        private void SetupTrayIcon()
        {
            try
            {
                _trayIcon = new Forms.NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Application,
                    Visible = true,
                    Text = "Claude Model Picker"
                };

                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("Exit", null, (_, _) => Shutdown());
                _trayIcon.ContextMenuStrip = menu;
            }
            catch (Exception ex)
            {
                // Tray icon is a convenience, not a dependency — app keeps running headless if this fails.
                _fileLogger?.LogError("App.SetupTrayIcon", ex);
            }
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
            _fileLogger?.LogEvent("APP_EXITING");
            _hookService?.Stop();
            _flaUiFallback?.Dispose();
            _configManager?.Dispose();
            _trayIcon?.Dispose();
            _fileLogger?.Dispose();
            base.OnExit(e);
        }
    }
}
