using System;
using System.Windows;
using ClaudeModelPicker.Services;

namespace ClaudeModelPicker
{
    public partial class App : Application
    {
        private KeyboardHookService? _hookService;
        private ClaudeMonitorService? _monitorService;
        private ConfigManager? _configManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Hide main window on startup (tray app)
            MainWindow.Hide();
            MainWindow.WindowState = WindowState.Minimized;

            try
            {
                _configManager = new ConfigManager();
                _monitorService = new ClaudeMonitorService(_configManager);
                _monitorService.Logger.CleanupOldLogs(_configManager.AppLogsToKeepDays);
                _hookService = new KeyboardHookService(_monitorService, _configManager);
                _hookService.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start hook service: {ex.Message}", "Error");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _hookService?.Stop();
            _monitorService?.Dispose();
            _configManager?.Dispose();
            base.OnExit(e);
        }
    }
}
