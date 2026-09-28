# OPUS REVIEW — ClaudeModelPicker

Reviewed: 2026-09-28, branch `sonnet-build-fixes` at commit `07321a7`
Method: static read of every source file on that branch, plus Jason's local build result (`dotnet build` passes, 1 warning).
Not reviewed: the 4 uncommitted local changes on Jason's PC (ModelPickDialog.xaml, FlaUIInputService.cs, KeyboardHookService.cs, PromptAnalyzerTests.cs). Commit and push them for a follow-up pass.

> v1 of this file reviewed `main` by mistake. Sonnet's work is on `sonnet-build-fixes`, not `main`. This version replaces it.

## Verdict: NO-SHIP

Sonnet fixed the build and did the service split well. The core flow still cannot work, and the new keyboard/clipboard path makes it unsafe if it ever starts working, because it types into whatever window has focus.

## 1. What Sonnet fixed (confirmed)

- SharpToken package, `KeyCode.VcEnter` + `VcNumPadEnter`, tests in their own csproj with `<Compile Remove="Tests\**" />`, StackPanel `Spacing` removed. Build passes.
- ClaudeMonitorService is now a thin facade over ConfigManager, FileLogger, PromptAnalyzer and InputMethodManager. Good split, easy to test.
- Config is wired: keywords, token thresholds, auto-accept confidence, Enter / Shift+Enter switches, method enable flags.
- FileLogger is wired and old logs are cleaned on start.
- FlaUI is now an off-by-default fallback.

## 2. Will not start as built (fix first, about 20 minutes)

**S1. config.json is not copied to the output folder.** ConfigManager reads `AppContext.BaseDirectory\config.json` and throws if it is missing. The csproj has no copy rule, so on start you get the "Failed to start hook service" message box and nothing runs. Fix in the csproj:

```xml
<ItemGroup>
  <None Update="config.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Check: `F:\APP-DEV\projects\ClaudeModelPicker\bin\Debug\net8.0-windows\win-x64\config.json` should exist after the next build.

**S2. `MainWindow` is null in OnStartup.** App.xaml still sets `StartupUri`, and WPF creates that window after `OnStartup` returns, so `MainWindow.Hide()` throws before the try block. Fix: remove `StartupUri`, set `ShutdownMode="OnExplicitShutdown"`, delete the MainWindow lines. A tray app needs no window.

## 3. Critical design problems

**D1. The hook still observes Enter instead of blocking it.** `KeyPressed` fires as Enter reaches Claude Desktop. Claude sends the prompt and clears the box first. Then:
- the clipboard read does Ctrl+A, Ctrl+C on an empty box and returns nothing, and
- any model switch applies to the next message, not this one.

Fix: in the handler set `e.SuppressEvent = true` (SharpHook supports this on Windows), do the work off the hook thread, then re-send Enter.

**D2. No foreground check, and the new path types keys.** The hook fires on Enter in every app. Once D1 and D3 are fixed, pressing Enter in any window would send Ctrl+A, Ctrl+C, then 5 Tabs, Space, an arrow and Enter into that window. In a terminal, Ctrl+C kills the running process. In a chat app, Enter sends a message. First line of the handler must be: foreground window belongs to Claude Desktop (`GetForegroundWindow` + process name), else return. This is also the audit's P1 focus check, still open.

**D3. Clipboard and SendKeys run on the hook thread.** SharpHook runs handlers on a thread-pool thread (MTA). WinForms `Clipboard` needs an STA thread and throws ThreadStateException there. The catch swallows it and returns "", so today the app most likely reads nothing and does nothing. This is why it looks safe right now. The same applies to `ShowPopup` building a WPF window off the UI thread. Fix: marshal all of it to the WPF dispatcher (`Application.Current.Dispatcher.InvokeAsync`).

**D4. The app will trigger itself.** KeyboardModelSelector ends with `SendKeys.SendWait("{ENTER}")`. The global hook sees injected keys too, so that Enter fires OnKeyPressed again, which reads, analyses and selects again. `throttleMs` exists in config but nothing uses it. Fix: a busy flag set before the work and cleared after, plus ignore events while it is set.

**D5. Keyboard selection is blind.** It sends a fixed 5 Tabs (unverified), Space, one arrow key, Enter, then always returns true. It cannot detect failure, so the popup fallback never runs. Up/Down only works for exactly two models in a fixed order. There is no way to confirm which model got picked without reading the screen.

## 4. Still open from v1

- **Popup choice goes nowhere.** `GetSelectedModel()` is never called. No timeout or auto-accept.
- **Only Haiku and Sonnet.** No Opus.
- **Keyword substring matches.** "refactor" contains "fact" (Haiku keyword), "list" matches "checklist". Use `\bword\b`.
- **Confidence hits 100 % on one-sided signals.** "list my files" normalises to 1.0 and takes the auto-click path. Score weights (0.4 / 0.5 / 0.4) are still hardcoded, config's `scoringWeights` is unused.
- **Tokenizer rebuilt on every Enter.** Cache it in a static field, or drop it: `Length / 4` is enough for a 150/300 threshold.
- **Prompt text written to disk.** P1 is now worse than on main, because FileLogger is wired: `LogAnalysis` writes the first 50 chars of every prompt to `%AppData%\ClaudeModelPicker\logs`. No sanitisation exists. Safest fix: log length, tokens, model and confidence only.
- **Admin claim in the audit is wrong.** Low-level keyboard hooks do not need admin. Fix the README and audit.
- `_pickLog` has no lock, log cleanup uses file creation time, FileSystemWatcher needs a debounce, no tray icon so no way to exit.

## 5. Order of work

1. S1 and S2. Goal: app starts and stays running. **About 20 minutes.**
2. D2 foreground check and D4 busy flag. These make it safe before it works. **About 20 minutes.**
3. D1 and D3: suppress Enter, move work to the dispatcher, re-send Enter. Test on real Claude Desktop. **This is the go/no-go gate.** If Claude's Electron input does not accept a re-sent Enter cleanly, switch the trigger to a hotkey (for example Ctrl+Shift+Enter) instead of hijacking Enter. **1 to 2 hours plus testing.**
4. D5: measure the real Tab count, add Opus, make selection verifiable or drop auto-select and always show the popup. **1 to 2 hours.**
5. Section 4 items. **About 1 to 2 hours.**
