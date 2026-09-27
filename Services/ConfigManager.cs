using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Loads and manages application configuration from config.json.
    /// Supports hot-reloading on file changes.
    /// </summary>
    public class ConfigManager
    {
        private readonly string _configPath;
        private JObject _config;
        private FileSystemWatcher _watcher;

        public event EventHandler ConfigReloaded;

        public ConfigManager(string configFileName = "config.json")
        {
            _configPath = Path.Combine(AppContext.BaseDirectory, configFileName);

            if (!File.Exists(_configPath))
            {
                throw new FileNotFoundException($"Config file not found: {_configPath}");
            }

            Load();
            WatchConfigFile();
        }

        /// <summary>
        /// Load or reload configuration from disk.
        /// </summary>
        public void Load()
        {
            try
            {
                var json = File.ReadAllText(_configPath);
                _config = JObject.Parse(json);
                System.Diagnostics.Debug.WriteLine("Config loaded successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Config load error: {ex.Message}");
                throw;
            }
        }

        // App settings
        public bool AppEnableOnStartup => GetBool("app.enableOnStartup", true);
        public bool AppMinimizeToTray => GetBool("app.minimizeToTray", true);
        public string AppLogLevel => GetString("app.logLevel", "Info");
        public int AppLogsToKeepDays => GetInt("app.logsToKeepDays", 30);

        // Analysis thresholds
        public int AnalysisHaikuMaxTokens => GetInt("analysis.tokenCountThreshold.haikuMax", 150);
        public int AnalysisSonnetMinTokens => GetInt("analysis.tokenCountThreshold.sonnetMin", 300);

        /// <summary>
        /// Get haiku keywords from config.
        /// </summary>
        public string[] AnalysisHaikuKeywords
        {
            get
            {
                var tokens = _config?["analysis"]?["keywords"]?["haiku"];
                if (tokens == null)
                    return Array.Empty<string>();

                return tokens.Values<string>().ToArray();
            }
        }

        /// <summary>
        /// Get sonnet keywords from config.
        /// </summary>
        public string[] AnalysisSonnetKeywords
        {
            get
            {
                var tokens = _config?["analysis"]?["keywords"]?["sonnet"];
                if (tokens == null)
                    return Array.Empty<string>();

                return tokens.Values<string>().ToArray();
            }
        }

        public double AnalysisConfidenceThreshold => GetDouble("analysis.confidenceThreshold", 0.6);

        // Model selection
        public int ModelSelectionThrottleMs => GetInt("modelSelection.throttleMs", 500);
        public string ModelSelectionInputMethod => GetString("modelSelection.inputMethod", "hybrid");

        public bool ClipboardEnabled => GetBool("modelSelection.methods.clipboard.enabled", true);
        public bool ClipboardRestoreContent => GetBool("modelSelection.methods.clipboard.restoreClipboard", true);

        public bool KeyboardEnabled => GetBool("modelSelection.methods.keyboard.enabled", true);
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
                _watcher = new FileSystemWatcher(Path.GetDirectoryName(_configPath))
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
