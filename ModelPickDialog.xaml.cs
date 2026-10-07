using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ClaudeModelPicker.Models;

namespace ClaudeModelPicker
{
    /// <summary>
    /// Asks which model to use. The user's Enter is held back while this is
    /// open, so it closes by itself after <c>modelSelection.popup.timeoutMs</c>
    /// (counts as Skip). The Skip button shows the seconds left.
    /// </summary>
    public partial class ModelPickDialog : Window
    {
        private string? _selectedModel;
        private readonly DispatcherTimer? _countdown;
        private int _secondsLeft;

        public ModelPickDialog(ModelPick pick, int timeoutMs = 0)
        {
            InitializeComponent();

            TitleText.Text = $"Recommended: {pick.PickedModel}";
            ReasoningText.Text = pick.Reasoning;

            // Highlight the recommended button
            var recommended = pick.PickedModel switch
            {
                "Opus" => OpusButton,
                "Sonnet" => SonnetButton,
                _ => HaikuButton
            };
            recommended.FontWeight = FontWeights.Bold;

            if (timeoutMs > 0)
            {
                _secondsLeft = (int)Math.Ceiling(timeoutMs / 1000.0);
                UpdateSkipLabel();
                _countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _countdown.Tick += (_, _) =>
                {
                    _secondsLeft--;
                    if (_secondsLeft <= 0) Choose(null);
                    else UpdateSkipLabel();
                };
                _countdown.Start();
            }
        }

        private void UpdateSkipLabel() => SkipButton.Content = $"Skip ({_secondsLeft})";

        private void Choose(string? model)
        {
            _countdown?.Stop();
            _selectedModel = model;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _countdown?.Stop();
            base.OnClosed(e);
        }

        private void OnHaikuClick(object sender, RoutedEventArgs e) => Choose("Haiku");

        private void OnSonnetClick(object sender, RoutedEventArgs e) => Choose("Sonnet");

        private void OnOpusClick(object sender, RoutedEventArgs e) => Choose("Opus");

        private void OnSkipClick(object sender, RoutedEventArgs e) => Choose(null);

        public string? GetSelectedModel() => _selectedModel;
    }
}
