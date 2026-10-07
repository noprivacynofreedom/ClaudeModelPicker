using System;
using System.IO;
using System.Threading;

namespace ClaudeModelPicker.Services
{
    public enum PickerMode
    {
        /// <summary>Hold Enter, read the prompt, recommend and switch the model, then send.</summary>
        Active,
        /// <summary>Do nothing on Enter: Claude sends the message as normal.</summary>
        Passive
    }

    /// <summary>
    /// The picker mode, kept in %AppData%\ClaudeModelPicker\mode.txt (one word:
    /// "active" or "passive"). ClaudeUsageMonitor writes the same file from its
    /// window, and the tray menu writes it here. A watcher picks up changes
    /// while the app runs. Missing or unknown text means Active.
    /// </summary>
    public sealed class ModeStore : IDisposable
    {
        public static string DefaultPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ClaudeModelPicker", "mode.txt");

        private readonly string _path;
        private FileSystemWatcher? _watcher;
        private volatile PickerMode _mode;

        /// <summary>Raised (on a worker thread) when the mode changes.</summary>
        public event Action<PickerMode>? Changed;

        public ModeStore(string? path = null, bool watch = true)
        {
            _path = path ?? DefaultPath;
            _mode = Read(_path);
            if (watch) Watch();
        }

        /// <summary>Read on the hook thread: a field read, no file I/O.</summary>
        public PickerMode Mode => _mode;

        public static PickerMode Parse(string? text) =>
            string.Equals(text?.Trim(), "passive", StringComparison.OrdinalIgnoreCase)
                ? PickerMode.Passive
                : PickerMode.Active;

        public static string Format(PickerMode mode) => mode == PickerMode.Passive ? "passive" : "active";

        public static PickerMode Read(string path)
        {
            try
            {
                return File.Exists(path) ? Parse(File.ReadAllText(path)) : PickerMode.Active;
            }
            catch
            {
                return PickerMode.Active;
            }
        }

        public void Set(PickerMode mode)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, Format(mode));
            }
            catch
            {
                // Still switch for this run even if the file cannot be written.
            }
            Update(mode);
        }

        private void Update(PickerMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            Changed?.Invoke(mode);
        }

        private void Watch()
        {
            try
            {
                var dir = Path.GetDirectoryName(_path)!;
                Directory.CreateDirectory(dir);
                _watcher = new FileSystemWatcher(dir)
                {
                    Filter = Path.GetFileName(_path),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                FileSystemEventHandler reload = (_, _) =>
                {
                    Thread.Sleep(50); // let the writer finish
                    Update(Read(_path));
                };
                _watcher.Changed += reload;
                _watcher.Created += reload;
                _watcher.Deleted += reload;
                _watcher.Renamed += (_, _) => Update(Read(_path));
                _watcher.EnableRaisingEvents = true;
            }
            catch
            {
                // No live reload: the mode from startup (or the tray) still applies.
            }
        }

        public void Dispose() => _watcher?.Dispose();
    }
}
