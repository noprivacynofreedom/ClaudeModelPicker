using ClaudeModelPicker.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Facade used by KeyboardHookService and App.xaml.cs. Internally this
    /// composes: ConfigManager (settings), FileLogger (structured logging),
    /// PromptAnalyzer (model pick logic), and InputMethodManager (clipboard
    /// + keyboard nav, with FlaUI as an off-by-default fallback).
    /// </summary>
    public class ClaudeMonitorService : IDisposable
    {
        private readonly ConfigManager _config;
        private readonly FileLogger _logger;
        public FileLogger Logger => _logger;
        private readonly FlaUIInputService _flaUi;
        private readonly InputMethodManager _inputManager;
        private readonly PromptAnalyzer _analyzer;
        private List<ModelPickLog> _pickLog = new();

        public ClaudeMonitorService(ConfigManager? config = null, FileLogger? logger = null)
        {
            _config = config ?? new ConfigManager();
            _logger = logger ?? new FileLogger();
            _flaUi = new FlaUIInputService();
            _inputManager = new InputMethodManager(_config, _flaUi, _logger);
            _analyzer = new PromptAnalyzer(_config);
        }

        public string ReadClaudeInputField() => _inputManager.ReadPrompt();

        public ModelPick AnalyzePrompt(string prompt)
        {
            var pick = _analyzer.Analyze(prompt);
            if (_config.LogEventAnalysis)
                _logger.LogAnalysis(prompt, pick.PickedModel, pick.Confidence, pick.TokenCount);
            return pick;
        }

        public bool TryClickModelDropdown(string model) => _inputManager.SelectModel(model);

        public void ShowPopup(ModelPick pick)
        {
            try
            {
                var dialog = new ModelPickDialog(pick);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing popup: {ex.Message}");
                if (_config.LogEventErrors)
                    _logger.LogError("ShowPopup", ex);
            }
        }

        public void LogModelPick(string prompt, ModelPick pick)
        {
            _pickLog.Add(new ModelPickLog
            {
                Timestamp = DateTime.Now,
                Prompt = prompt.Substring(0, Math.Min(100, prompt.Length)),
                PickedModel = pick.PickedModel,
                Confidence = pick.Confidence
            });

            if (_pickLog.Count > 1000)
                _pickLog = _pickLog.TakeLast(1000).ToList();
        }

        public List<ModelPickLog> GetPickHistory() => _pickLog;

        public void Dispose()
        {
            _flaUi.Dispose();
            _logger.Dispose();
        }
    }
}
