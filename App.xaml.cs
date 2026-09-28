using System;
using System.Security.Principal;
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

            MainWindow.Hide();
            MainWindow.WindowState = WindowState.Minimized;

            try
            {
                _fileLogger = new FileLogger();
                _configManager = new ConfigManager();

                _fileLogger.CleanupOldLogs(_configManager.AppLogsToKeepDays);

                WarnIfNotAdmin();

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
            }
        }

        /// <summary>
        /// Global keyboard hooks intercept the calling process's window at
        /// the same or higher integrity level. If Claude Desktop or Windows
        /// is elevated and this app isn't, the hook installs without error
        /// but silently never fires for that window. Warn once at startup
        /// rather than leave the user wondering why nothing happens — see
        /// SECURITY-AUDIT.md "Admin privilege requirements", flagged as
        /// "needs documenting, not fixing."
        /// </summary>
        private void WarnIfNotAdmin()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                var isAdmin = principal.IsInRole(WindowsBuiltInRole.Administrator);

                if (!isAdmin)
                {
                    _fileLogger?.LogEvent("STARTUP_WARNING", ("reason", "not_running_as_admin"));
                    MessageBox.Show(
                        "Claude Model Picker is not running as Administrator.\n\n" +
                        "If Claude Desktop is running elevated, this app's keyboard hook " +
                        "will not be able to intercept its keystrokes. If Enter presses in " +
                        "Claude Desktop don't trigger a popup, try restarting this app as Administrator.",
                        "Claude Model Picker — Admin Notice",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                _fileLogger?.LogError("App.WarnIfNotAdmin", ex);
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
