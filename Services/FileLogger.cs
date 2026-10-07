using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// File-based structured logger for ClaudeModelPicker events.
    /// Logs to %AppData%\ClaudeModelPicker\logs\ with daily file rotation.
    /// </summary>
    public class FileLogger : IDisposable
    {
        private readonly string _logDirectory;
        private readonly object _lockObj = new();

        public FileLogger()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeModelPicker",
                "logs"
            );

            // Ensure directory exists
            if (!Directory.Exists(_logDirectory))
                Directory.CreateDirectory(_logDirectory);
        }

        /// <summary>
        /// Log an event with timestamp, action, and optional details.
        /// Format: [TIMESTAMP] ACTION | Key1=Value1, Key2=Value2, ...
        /// </summary>
        public void LogEvent(string action, params (string key, string value)[] details)
        {
            lock (_lockObj)
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var detailsStr = details.Length > 0
                        ? " | " + string.Join(", ", details.Select(d => $"{d.key}={d.value}"))
                        : string.Empty;

                    var line = $"[{timestamp}] {action}{detailsStr}";

                    AppendLine(line);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Log a prompt analysis event.
        /// </summary>
        public void LogAnalysis(string prompt, string pickedModel, double confidence, int tokenCount)
        {
            var promptPreview = prompt.Length > 50 ? prompt.Substring(0, 50) + "..." : prompt;
            LogEvent("ANALYSIS",
                ("model", pickedModel),
                ("confidence", confidence.ToString("P0")),
                ("tokens", tokenCount.ToString()),
                ("prompt", promptPreview)
            );
        }

        /// <summary>
        /// Log a model selection event.
        /// </summary>
        public void LogSelection(string model, string method, bool success)
        {
            LogEvent("SELECT",
                ("model", model),
                ("method", method),
                ("success", success.ToString())
            );
        }

        /// <summary>
        /// Log an error event.
        /// </summary>
        public void LogError(string context, Exception ex)
        {
            LogEvent("ERROR",
                ("context", context),
                ("exception", ex.GetType().Name),
                ("message", ex.Message)
            );
        }

        /// <summary>
        /// Log keyboard hook event.
        /// </summary>
        public void LogHook(bool triggered, string reason = "")
        {
            var details = new List<(string, string)>
            {
                ("triggered", triggered.ToString())
            };

            if (!string.IsNullOrEmpty(reason))
                details.Add(("reason", reason));

            LogEvent("HOOK", details.ToArray());
        }

        /// <summary>
        /// Get all log entries for the current day.
        /// </summary>
        public string[] ReadTodayLogs()
        {
            lock (_lockObj)
            {
                try
                {
                    var logFile = GetLogFilePath(DateTime.Now);
                    if (!File.Exists(logFile))
                        return Array.Empty<string>();

                    return File.ReadAllLines(logFile);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Read logs error: {ex.Message}");
                    return Array.Empty<string>();
                }
            }
        }

        /// <summary>
        /// Get log files for a date range (for analytics/reporting).
        /// </summary>
        public Dictionary<string, string[]> ReadLogsForDateRange(DateTime startDate, DateTime endDate)
        {
            var result = new Dictionary<string, string[]>();
            lock (_lockObj)
            {
                for (var date = startDate; date <= endDate; date = date.AddDays(1))
                {
                    var logFile = GetLogFilePath(date);
                    if (File.Exists(logFile))
                    {
                        try
                        {
                            result[date.ToString("yyyy-MM-dd")] = File.ReadAllLines(logFile);
                        }
                        catch { }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Delete old log files (older than specified days).
        /// </summary>
        public void CleanupOldLogs(int daysToKeep = 30)
        {
            lock (_lockObj)
            {
                try
                {
                    var cutoffDate = DateTime.Now.AddDays(-daysToKeep);
                    foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
                    {
                        // Name is yyyy-MM-dd.log. Creation time changes when a file is copied, so prefer the name.
                        var fileDate = DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out var named)
                            ? named
                            : File.GetCreationTime(file);
                        if (fileDate < cutoffDate)
                            File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
                }
            }
        }

        // Private helpers

        /// <summary>
        /// Opens, appends, closes. A writer held open for the whole run would
        /// block (or overwrite) lines from a second process: "--select" runs as
        /// its own process while the tray app is logging to the same file.
        /// </summary>
        private void AppendLine(string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line + Environment.NewLine);
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(GetLogFilePath(DateTime.Now), FileMode.Append,
                        FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                    stream.Write(bytes, 0, bytes.Length);
                    return;
                }
                catch (IOException) when (attempt < 3)
                {
                    System.Threading.Thread.Sleep(15);
                }
            }
        }

        private string GetLogFilePath(DateTime date)
        {
            var fileName = $"{date:yyyy-MM-dd}.log";
            return Path.Combine(_logDirectory, fileName);
        }

        public void Dispose()
        {
            // Nothing held open: see AppendLine.
        }
    }
}
