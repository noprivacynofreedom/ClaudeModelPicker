using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ClaudeModelPicker.Services
{
    /// <summary>
    /// UI Automation reader/clicker for Claude Desktop (FlaUI).
    ///
    /// Model switching is verified on the real app: the model button exposes
    /// ExpandCollapse and each model is a RadioButton with Invoke (see
    /// <see cref="ModelNames"/> for the real element names). Prompt reading is
    /// a fallback behind the clipboard path in <see cref="InputMethodManager"/>.
    /// Only UIA patterns are used. No mouse, no Tab, no arrow keys.
    /// </summary>
    public class ClaudeMonitorService : IDisposable
    {
        private const string ElectronWindowClass = "Chrome_WidgetWin_1";
        private const int MenuPollMs = 150;
        private const int MenuPollCount = 10;

        private readonly UIA3Automation _automation;
        private readonly FileLogger? _logger;

        public ClaudeMonitorService(FileLogger? logger = null)
        {
            _automation = new UIA3Automation();
            _logger = logger;
        }

        /// <summary>
        /// Top-level Electron window owned by a "claude" process. Matching on
        /// the process (not only the title) skips browser tabs called "Claude".
        /// </summary>
        private AutomationElement? FindClaudeWindow()
        {
            var pids = new HashSet<int>();
            foreach (var p in Process.GetProcessesByName("claude"))
            {
                pids.Add(p.Id);
                p.Dispose();
            }
            if (pids.Count == 0) return null;

            var windows = _automation.GetDesktop().FindAllChildren()
                .Where(w => pids.Contains(w.Properties.ProcessId.ValueOrDefault) &&
                            w.Properties.ClassName.ValueOrDefault == ElectronWindowClass)
                .ToArray();

            return windows.FirstOrDefault(w => w.Properties.Name.ValueOrDefault == "Claude")
                ?? windows.FirstOrDefault();
        }

        /// <summary>
        /// Attempts to read the current prompt text via UI Automation.
        /// Returns empty string if the Claude window, or any input control
        /// inside it, cannot be found.
        /// </summary>
        public string ReadClaudeInputFieldViaFlaUI()
        {
            try
            {
                var claudeWindow = FindClaudeWindow();
                if (claudeWindow == null)
                    return string.Empty;

                var input = FindPromptBox(claudeWindow);
                if (input != null)
                    return input.AsTextBox()?.Text ?? string.Empty;

                var texts = claudeWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                return texts.Length == 0 ? string.Empty : texts.Last().AsTextBox()?.Text ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger?.LogError("ClaudeMonitorService.ReadClaudeInputFieldViaFlaUI", ex);
                return string.Empty;
            }
        }

        /// <summary>
        /// Switches Claude Desktop's model picker to <paramref name="model"/>
        /// ("Haiku", "Sonnet" or "Opus"). Returns true only when the model
        /// button shows that model afterwards. Every failure is logged with
        /// its reason (and the menu item names seen) so it can be fixed from
        /// the log alone.
        /// </summary>
        public bool TryClickModelDropdownViaFlaUI(string model)
        {
            try
            {
                var claudeWindow = FindClaudeWindow();
                if (claudeWindow == null)
                    return Fail(model, "no_claude_window");

                var modelButton = FindModelButton(claudeWindow);
                if (modelButton == null)
                    return Fail(model, "no_model_button");

                var before = modelButton.Properties.Name.ValueOrDefault;
                if (ModelNames.ButtonShows(before, model))
                {
                    _logger?.LogEvent("FLAUI_SELECT", ("model", model), ("result", "already_selected"));
                    return true;
                }

                var expand = modelButton.Patterns.ExpandCollapse.PatternOrDefault;
                if (expand == null)
                    return Fail(model, "button_not_expandable", ("button", before ?? ""));

                expand.Expand();

                AutomationElement? target = null;
                var seen = Array.Empty<string>();
                var openedMore = false;
                for (var i = 0; i < MenuPollCount && target == null; i++)
                {
                    Thread.Sleep(MenuPollMs);
                    var items = claudeWindow.FindAllDescendants(cf =>
                        cf.ByControlType(ControlType.RadioButton).Or(cf.ByControlType(ControlType.MenuItem)));
                    seen = items.Select(e => e.Properties.Name.ValueOrDefault ?? "").ToArray();
                    target = items.FirstOrDefault(e => ModelNames.IsMenuItemFor(e.Properties.Name.ValueOrDefault, model));

                    // Some models sit in a "More models" submenu. Open it once, then keep polling.
                    if (target == null && !openedMore && i > 0)
                    {
                        var more = items.FirstOrDefault(e => ModelNames.IsMoreModelsItem(e.Properties.Name.ValueOrDefault));
                        var moreExpand = more?.Patterns.ExpandCollapse.PatternOrDefault;
                        if (moreExpand != null)
                        {
                            moreExpand.Expand();
                            openedMore = true;
                        }
                    }
                }

                if (target == null)
                {
                    TryCollapse(expand);
                    return Fail(model, "no_menu_item", ("button", before ?? ""), ("items_seen", Summarize(seen)));
                }

                var invoke = target.Patterns.Invoke.PatternOrDefault;
                var select = target.Patterns.SelectionItem.PatternOrDefault;
                if (invoke != null) invoke.Invoke();
                else if (select != null) select.Select();
                else
                {
                    TryCollapse(expand);
                    return Fail(model, "item_not_invokable", ("item", target.Properties.Name.ValueOrDefault ?? ""));
                }

                Thread.Sleep(200);
                TryCollapse(expand);

                // Put the caret back in the prompt box so the re-sent Enter still sends the message.
                FindPromptBox(claudeWindow)?.Focus();

                var after = FindModelButton(claudeWindow)?.Properties.Name.ValueOrDefault;
                var ok = ModelNames.ButtonShows(after, model);
                _logger?.LogEvent("FLAUI_SELECT", ("model", model),
                    ("result", ok ? "switched" : "not_confirmed"),
                    ("before", before ?? ""), ("after", after ?? ""));
                return ok;
            }
            catch (Exception ex)
            {
                _logger?.LogError("ClaudeMonitorService.TryClickModelDropdownViaFlaUI", ex);
                return false;
            }
        }

        private static AutomationElement? FindModelButton(AutomationElement window) =>
            window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                .FirstOrDefault(b => ModelNames.IsModelButton(b.Properties.Name.ValueOrDefault));

        /// <summary>The prompt box is an Edit named "Write your prompt to Claude"; else the last Edit.</summary>
        private static AutomationElement? FindPromptBox(AutomationElement window)
        {
            var edits = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
            return edits.FirstOrDefault(e => (e.Properties.Name.ValueOrDefault ?? "")
                       .StartsWith("Write your prompt", StringComparison.OrdinalIgnoreCase))
                ?? edits.LastOrDefault();
        }

        private static void TryCollapse(FlaUI.Core.Patterns.IExpandCollapsePattern expand)
        {
            try
            {
                if (expand.ExpandCollapseState.Value == ExpandCollapseState.Expanded)
                    expand.Collapse();
            }
            catch { /* menu already closed */ }
        }

        private bool Fail(string model, string reason, params (string key, string value)[] extra)
        {
            var details = new List<(string, string)> { ("model", model), ("result", "failed"), ("reason", reason) };
            details.AddRange(extra);
            _logger?.LogEvent("FLAUI_SELECT", details.ToArray());
            return false;
        }

        // Menu item names are UI labels, not prompt text: safe to log, but keep them short.
        private static string Summarize(string[] names) =>
            names.Length == 0
                ? "none"
                : string.Join(" / ", names.Take(10).Select(n => n.Length > 30 ? n.Substring(0, 30) : n));

        public void Dispose()
        {
            _automation?.Dispose();
        }
    }
}
