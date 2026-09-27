using Newtonsoft.Json;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// Loads config.json from %AppData%\ClaudeModelPicker\config.json.
    /// Falls back to hard-coded defaults (the values currently baked into
    /// PromptAnalyzer and KeyboardHookService) if the file is missing or
    /// fails to parse, so the app never crashes on a bad config edit.
    /// </summary>
    public class AppConfig
    {
        public string[] HaikuKeywords { get; set; } =
            { "summarize", "extract", "list", "fact", "code snippet", "quick", "simple" };

        public string[] SonnetKeywords { get; set; } =
            { "analyze", "compare", "design", "debug", "complex", "explain", "reason", "architecture" };

        public int TokenThresholdLow { get; set; } = 150;
        public int TokenThresholdHigh { get; set; } = 300;

        public double TokenScoreWeight { get; set; } = 0.4;
        public double SonnetKeywordScoreWeight { get; set; } = 0.5;
        public double HaikuKeywordScoreWeight { get; set; } = 0.4;

        public double AutoClickConfidenceThreshold { get; set; } = 0.8;
        public bool AutoClickEnabled { get; set; } = false;

        public int ModelSelectorTabStops { get; set; } = 3;

        public int EnterThrottleMs { get; set; } = 500;

        public string LogLevel { get; set; } = "Info";
        public int LogRetentionDays { get; set; } = 30;
    }

    public static class ConfigManager
    {
        private static AppConfig? _current;
        public static AppConfig Current => _current ??= Load();

        private static string ConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeModelPicker",
            "config.json");

        public static AppConfig Load()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath)!;
                Directory.CreateDirectory(dir);

                if (!File.Exists(ConfigPath))
                {
                    var defaults = new AppConfig();
                    Save(defaults); // write out defaults on first run so the user has something to edit
                    return defaults;
                }

                var json = File.ReadAllText(ConfigPath);
                var config = JsonConvert.DeserializeObject<AppConfig>(json);
                return config ?? new AppConfig();
            }
            catch
            {
                // Malformed config.json should never crash the app — fall back silently.
                // (Sonnet: pair this with a FileLogger.LogWarning call once FileLogger
                // is wired into the DI/constructor chain — ConfigManager itself is static
                // and loads before any logger exists, so it can't log its own failure.)
                return new AppConfig();
            }
        }

        public static void Save(AppConfig config)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath)!;
                Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // best effort — see Load()
            }
        }

        /// <summary>Force a re-read from disk, e.g. after a settings window saves changes.</summary>
        public static void Reload() => _current = Load();
    }
}
