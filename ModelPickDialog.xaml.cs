using System.Windows;
using ClaudeModelPicker.Models;
using ClaudeModelPicker.Services;

namespace ClaudeModelPicker
{
    public partial class ModelPickDialog : Window
    {
        private readonly ModelPick _pick;
        private string? _selectedModel;

        public ModelPickDialog(ModelPick pick)
        {
            InitializeComponent();
            _pick = pick;

            TitleText.Text = $"Recommended: {pick.PickedModel}";
            ReasoningText.Text = pick.Reasoning;

            // Highlight the recommended button
            if (pick.PickedModel == "Haiku")
                HaikuButton.FontWeight = FontWeights.Bold;
            else
                SonnetButton.FontWeight = FontWeights.Bold;
        }

        private void OnHaikuClick(object sender, RoutedEventArgs e)
        {
            _selectedModel = "Haiku";
            Close();
        }

        private void OnSonnetClick(object sender, RoutedEventArgs e)
        {
            _selectedModel = "Sonnet";
            Close();
        }

        private void OnSkipClick(object sender, RoutedEventArgs e)
        {
            _selectedModel = null;
            Close();
        }

        public string? GetSelectedModel() => _selectedModel;
    }
}
