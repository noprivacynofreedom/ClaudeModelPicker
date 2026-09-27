using System.Windows;
using ClaudeModelPicker.Services;

namespace ClaudeModelPicker
{
    public partial class App : Application
    {
        private KeyboardHookService? _hookService;
        private ClaudeMonitorService? _monitorService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Hide main window on startup (tray app)
            MainWindow.Hide();
            MainWindow.WindowState = WindowState.Minimized;

            try
            {
                _monitorService = new ClaudeMonitorService();
                _hookService = new KeyboardHookService(_monitorService);
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
            base.OnExit(e);
        }
    }
}
