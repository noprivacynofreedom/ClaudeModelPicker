using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// One-shot UI Automation dump of Claude Desktop, run with
    /// "ClaudeModelPicker.exe --uia-dump". Writes every named element (type, name,
    /// id, class, supported patterns) to %AppData%\ClaudeModelPicker\uia-dump.txt
    /// twice: once with the model menu closed, once after opening it.
    /// The names in that file are what the real SelectModel is built from.
    /// It never selects a model and never sends keystrokes.
    /// </summary>
    public static class UiaDiagnostic
    {
        private const int MaxElements = 4000;
        private static readonly string[] ModelWords = { "opus", "sonnet", "haiku", "fable", "model" };

        public static string Run()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeModelPicker");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "uia-dump.txt");
            var sb = new StringBuilder();

            try
            {
                using var automation = new UIA3Automation();
                var pids = Process.GetProcessesByName("claude").Select(p => p.Id).ToHashSet();
                if (pids.Count == 0)
                {
                    sb.AppendLine("No process named 'claude' is running. Open Claude Desktop and run again.");
                    File.WriteAllText(path, sb.ToString());
                    return path;
                }

                var windows = ClaudeWindows(automation, pids);
                sb.AppendLine($"Claude windows found: {windows.Length}");
                sb.AppendLine();
                sb.AppendLine("===== PHASE 1: menu closed =====");
                Dump(windows, sb);

                AutomationElement? modelButton = null;
                foreach (var w in windows)
                {
                    modelButton = w.FindAllDescendants()
                        .Where(e => e.Properties.ControlType.ValueOrDefault == ControlType.Button)
                        .FirstOrDefault(e => ModelWords.Any(m =>
                            (e.Properties.Name.ValueOrDefault ?? "").Contains(m, StringComparison.OrdinalIgnoreCase)));
                    if (modelButton != null) break;
                }

                if (modelButton == null)
                {
                    sb.AppendLine();
                    sb.AppendLine("No Button with opus/sonnet/haiku/fable/model in its name. Look in PHASE 1 for the real name.");
                }
                else
                {
                    sb.AppendLine();
                    sb.AppendLine($"Model button used: '{modelButton.Properties.Name.ValueOrDefault}'");

                    var expand = modelButton.Patterns.ExpandCollapse.PatternOrDefault;
                    var invoke = modelButton.Patterns.Invoke.PatternOrDefault;
                    if (expand != null) expand.Expand();
                    else if (invoke != null) invoke.Invoke();
                    else sb.AppendLine("Button supports neither ExpandCollapse nor Invoke.");

                    Thread.Sleep(700);

                    // Electron menus can be a new top-level window, so list windows again.
                    var after = ClaudeWindows(automation, pids);
                    sb.AppendLine();
                    sb.AppendLine($"===== PHASE 2: menu open (Claude windows: {after.Length}) =====");
                    Dump(after, sb);

                    // Close the menu the same way we opened it.
                    if (expand != null) expand.Collapse();
                    else invoke?.Invoke();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine();
                sb.AppendLine("ERROR: " + ex);
            }

            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static AutomationElement[] ClaudeWindows(UIA3Automation automation, System.Collections.Generic.HashSet<int> pids)
        {
            return automation.GetDesktop().FindAllChildren()
                .Where(w => pids.Contains(w.Properties.ProcessId.ValueOrDefault))
                .ToArray();
        }

        private static void Dump(AutomationElement[] roots, StringBuilder sb)
        {
            var count = 0;
            foreach (var root in roots)
            {
                sb.AppendLine($"-- window '{root.Properties.Name.ValueOrDefault}' class '{root.Properties.ClassName.ValueOrDefault}'");
                foreach (var e in root.FindAllDescendants())
                {
                    if (++count > MaxElements)
                    {
                        sb.AppendLine("... truncated");
                        return;
                    }

                    var name = e.Properties.Name.ValueOrDefault;
                    var id = e.Properties.AutomationId.ValueOrDefault;
                    // Skip anonymous layout nodes so the file stays readable.
                    if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(id)) continue;

                    var patterns = string.Join(",", new[]
                    {
                        e.Patterns.Invoke.IsSupported ? "Invoke" : null,
                        e.Patterns.ExpandCollapse.IsSupported ? "ExpandCollapse" : null,
                        e.Patterns.SelectionItem.IsSupported ? "SelectionItem" : null,
                        e.Patterns.Value.IsSupported ? "Value" : null,
                    }.Where(p => p != null));

                    sb.AppendLine(
                        $"{e.Properties.ControlType.ValueOrDefault} | name='{Trim(name)}' | id='{id}' | class='{e.Properties.ClassName.ValueOrDefault}' | patterns=[{patterns}]");
                }
            }
        }

        private static string Trim(string? s) =>
            s == null ? "" : (s.Length > 120 ? s.Substring(0, 120) + "..." : s).Replace("\r", " ").Replace("\n", " ");
    }
}
