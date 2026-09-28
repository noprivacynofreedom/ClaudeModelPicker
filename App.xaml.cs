using System;
using System.Windows;
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

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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

        protected override void OnExit(ExitEventArgs e)
        {
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
