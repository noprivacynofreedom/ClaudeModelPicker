using System.Text;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// File-based logger. One line per event, date-rotated, append-only.
    /// Replaces the silent Debug.WriteLine calls scattered through
    /// ClaudeMonitorService and KeyboardHookService.
    /// </summary>
    public class FileLogger
    {
        private readonly string _logDirectory;
        private readonly object _writeLock = new();

        public FileLogger(string? logDirectory = null)
        {
            _logDirectory = logDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeModelPicker",
                "logs");

            Directory.CreateDirectory(_logDirectory);
        }

        public void LogInfo(string action, string result, string? detail = null) =>
            Write("INFO", action, result, detail);

        public void LogWarning(string action, string result, string? detail = null) =>
            Write("WARN", action, result, detail);

        public void LogError(string action, Exception ex) =>
            Write("ERROR", action, "failed", ex.Message);

        private void Write(string level, string action, string result, string? detail)
        {
            // Format: 2026-09-28T02:31:00+13:00 | INFO  | action=read      | result=ok       | detail=...
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" | ").Append(level.PadRight(5))
                .Append(" | action=").Append(action.PadRight(12))
                .Append(" | result=").Append(result.PadRight(10));

            if (!string.IsNullOrEmpty(detail))
                line.Append(" | detail=").Append(Sanitize(detail));

            var path = CurrentLogFilePath();

            lock (_writeLock)
            {
                try
                {
                    File.AppendAllText(path, line.ToString() + Environment.NewLine);
                }
                catch
                {
                    // Logging must never crash the app. If the log write fails
                    // (disk full, permissions, file locked), swallow it —
                    // there's no second-order logger to report the failure to.
                }
            }
        }

        private string CurrentLogFilePath() =>
            Path.Combine(_logDirectory, $"picker-{DateTime.Now:yyyy-MM-dd}.log");

        /// <summary>
        /// Strips anything that looks like an API key or bearer token before
        /// it hits disk. See docs/security-audit.md — prompt text itself is
        /// truncated by the caller (LogModelPick already does
        /// prompt.Substring(0, 100)); this catches secrets specifically.
        /// </summary>
        private static string Sanitize(string input)
        {
            // sk-ant-..., sk-..., Bearer <token>
            var result = System.Text.RegularExpressions.Regex.Replace(
                input, @"sk-[A-Za-z0-9\-_]{10,}", "[REDACTED-KEY]");
            result = System.Text.RegularExpressions.Regex.Replace(
                result, @"Bearer\s+[A-Za-z0-9\-_.]{10,}", "Bearer [REDACTED-TOKEN]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return result;
        }

        /// <summary>
        /// Deletes log files older than <paramref name="days"/>. Call once at
        /// startup — logs never expire otherwise and this is a background app
        /// meant to run for weeks.
        /// </summary>
        public void RotateOldLogs(int days = 30)
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-days);
                foreach (var file in Directory.GetFiles(_logDirectory, "picker-*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch
            {
                // best effort, same reasoning as Write()
            }
        }
    }
}
