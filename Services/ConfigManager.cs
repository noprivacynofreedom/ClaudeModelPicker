using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Loads and manages application configuration from config.json.
    /// Supports hot-reloading on file changes.
    /// </summary>
    public class ConfigManager : IDisposable
    {
        private readonly string _configPath;
        private JObject _config;
        private FileSystemWatcher? _watcher;

        public event EventHandler? ConfigReloaded;

        public ConfigManager(string configFileName = "config.json")
        {
            _configPath = Path.Combine(AppContext.BaseDirectory, configFileName);
            _config = new JObject(); // never null, even if Load() below fails entirely

            Load();
            WatchConfigFile();
        }

        /// <summary>
        /// Load or reload configuration from disk. Never throws: a missing or
        /// malformed config.json falls back to an empty JObject, and every
        /// Get*() accessor already has its own default, so the app runs with
        /// built-in defaults rather than crashing at startup. This was the
        /// single biggest ship blocker in the original version — a missing
        /// config.json (e.g. a fresh clone before the .csproj was fixed to
        /// copy it to the output directory) took down the whole app.
        /// </summary>
        public void Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Config file not found at {_configPath}; using built-in defaults.");
                    _config = new JObject();
                    return;
                }

                var json = File.ReadAllText(_configPath);
                _config = JObject.Parse(json);
                System.Diagnostics.Debug.WriteLine("Config loaded successfully");
            }
            catch (Exception ex)
            {
                // Malformed JSON, locked file, etc. — fall back to defaults
                // rather than propagate. Config is a convenience, not a
                // dependency the app should die over.
                System.Diagnostics.Debug.WriteLine($"Config load error: {ex.Message}");
                _config = new JObject();
            }
        }

        // App settings
        public bool AppEnableOnStartup => GetBool("app.enableOnStartup", true);
        public bool AppMinimizeToTray => GetBool("app.minimizeToTray", true);
        public string AppLogLevel => GetString("app.logLevel", "Info");
        public int AppLogsToKeepDays => GetInt("app.logsToKeepDays", 30);
        public int AppIdleShutdownMinutes => GetInt("app.idleShutdownMinutes", 10);

        // Analysis thresholds
        public int AnalysisHaikuMaxTokens => GetInt("analysis.tokenCountThreshold.haikuMax", 150);
        public int AnalysisSonnetMinTokens => GetInt("analysis.tokenCountThreshold.sonnetMin", 300);
        public int AnalysisOpusMinTokens => GetInt("analysis.tokenCountThreshold.opusMin", 1500);

        public string[] AnalysisHaikuKeywords => GetStrings("analysis.keywords.haiku");
        public string[] AnalysisSonnetKeywords => GetStrings("analysis.keywords.sonnet");
        public string[] AnalysisOpusKeywords => GetStrings("analysis.keywords.opus");

        public double AnalysisConfidenceThreshold => GetDouble("analysis.confidenceThreshold", 0.6);
        public double AnalysisTokenCountWeight => GetDouble("analysis.scoringWeights.tokenCount", 0.4);
        public double AnalysisKeywordsWeight => GetDouble("analysis.scoringWeights.keywords", 0.5);

        // Model selection
        public int ModelSelectionThrottleMs => GetInt("modelSelection.throttleMs", 500);
        public string ModelSelectionInputMethod => GetString("modelSelection.inputMethod", "hybrid");

        public bool ClipboardEnabled => GetBool("modelSelection.methods.clipboard.enabled", true);
        public bool ClipboardRestoreContent => GetBool("modelSelection.methods.clipboard.restoreClipboard", true);

        public bool KeyboardEnabled => GetBool("modelSelection.methods.keyboard.enabled", false);
        public int KeyboardTabCount => GetInt("modelSelection.methods.keyboard.tabCount", 5);
        public int KeyboardTimeoutMs => GetInt("modelSelection.methods.keyboard.timeoutMs", 2000);

        public bool FlaUIEnabled => GetBool("modelSelection.methods.flaui.enabled", false);

        public bool PopupShowConfirmation => GetBool("modelSelection.popup.showConfirmation", true);
        public double PopupAutoAcceptConfidence => GetDouble("modelSelection.popup.autoAcceptConfidence", 0.85);
        public int PopupTimeoutMs => GetInt("modelSelection.popup.timeoutMs", 5000);

        // Keyboard hook
        public bool KeyboardInterceptEnter => GetBool("keyboard.interceptEnter", true);
        public bool KeyboardInterceptShiftEnter => GetBool("keyboard.interceptShiftEnter", false);
        public bool KeyboardGlobalHookEnabled => GetBool("keyboard.globalHookEnabled", true);

        // Logging
        public bool LogEventHookFired => GetBool("logging.events.hookFired", true);
        public bool LogEventPromptRead => GetBool("logging.events.promptRead", true);
        public bool LogEventAnalysis => GetBool("logging.events.analysis", true);
        public bool LogEventModelSelection => GetBool("logging.events.modelSelection", true);
        public bool LogEventErrors => GetBool("logging.events.errors", true);

        // Utility methods

        private bool GetBool(string path, bool defaultValue)
        {
            try
            {
                var token = _config.SelectToken(path);
                return token?.Value<bool>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private int GetInt(string path, int defaultValue)
        {
            try
            {
                var token = _config.SelectToken(path);
                return token?.Value<int>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private double GetDouble(string path, double defaultValue)
        {
            try
            {
                var token = _config.SelectToken(path);
                return token?.Value<double>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        private string[] GetStrings(string path)
        {
            try
            {
                var token = _config.SelectToken(path);
                if (token == null) return Array.Empty<string>();
                return token.Values<string>()
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private string GetString(string path, string defaultValue)
        {
            try
            {
                var token = _config.SelectToken(path);
                return token?.Value<string>() ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        // File watcher for hot-reload

        private void WatchConfigFile()
        {
            try
            {
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory)
                {
                    Filter = Path.GetFileName(_configPath),
                    NotifyFilter = NotifyFilters.LastWrite
                };

                _watcher.Changed += (s, e) =>
                {
                    System.Threading.Thread.Sleep(100); // Wait for write to complete
                    try
                    {
                        Load();
                        ConfigReloaded?.Invoke(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Config reload error: {ex.Message}");
                    }
                };

                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"File watcher setup error: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _watcher?.Dispose();
        }
    }
}
