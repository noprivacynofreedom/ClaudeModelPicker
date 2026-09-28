# OPUS REVIEW — ClaudeModelPicker

Reviewed: 2026-09-28, static read of every source file on two branches:

| Branch | Commit | Built on Jason's PC? |
|---|---|---|
| `sonnet-build-fixes` | `34165b9` | Yes, 0 errors |
| `sonnet-fix-and-wire-integration` | `877e665` (newest, has the latest HANDOFF-OPUS.md) | Not yet |

> Earlier versions of this file reviewed `main`, then only `sonnet-build-fixes`. Two Sonnet sessions worked in parallel from `main`, and neither knew about the other. This version covers both.

## Verdict: NO-SHIP

Neither branch is shippable alone. Each one has half the fixes:

- `sonnet-build-fixes` builds, but cannot start (config.json not copied) and has no safety checks.
- `sonnet-fix-and-wire-integration` has the config copy, tray icon, throttle, async hook, dispatcher popup, sanitizer and a foreground check, but it **will not build**.

Both still share the core design flaw (D1): the hook sees Enter after Claude already sent the prompt.

## 0. Pick one base branch

**Recommendation: use `sonnet-fix-and-wire-integration` as the base** and port the build fixes from `sonnet-build-fixes` onto it. It has far more of the real work. The build fixes are 3 small edits (section 1). Then delete `sonnet-build-fixes` so there is one line of work again.

## 1. Build blockers on `sonnet-fix-and-wire-integration` (about 15 minutes)

**B1. `KeyCode.Return` does not exist in SharpHook 5.x.** Use `KeyCode.VcEnter` (and `VcNumPadEnter`). `sonnet-build-fixes` already has this.

**B2. Tests compile into the app.** The branch deleted `Tests/ClaudeModelPicker.Tests.csproj` and has no `<Compile Remove="Tests\**" />`, so `Tests/PromptAnalyzerTests.cs` and its `using Xunit;` get globbed into the WPF app. Take both the test csproj and the Compile Remove from `sonnet-build-fixes`.

**B3. Deprecated SDK attribute (warning only).** Change `Microsoft.NET.Sdk.WindowsDesktop` to `Microsoft.NET.Sdk`. Remove the unused `System.Management` package.

## 2. Will not start (both branches)

**S1. `MainWindow` is null in OnStartup.** App.xaml still sets `StartupUri`, and WPF creates that window after `OnStartup` returns. `MainWindow.Hide()` throws a NullReferenceException before the try block. Fix: remove `StartupUri`, add `ShutdownMode="OnExplicitShutdown"`, delete the two MainWindow lines. The tray icon now gives the app a presence, so MainWindow can go.

**S2. config.json not copied** (`sonnet-build-fixes` only, confirmed on Jason's PC: `Test-Path` returned False). Fixed on the integration branch.

## 3. Critical design problems

**D1. The hook observes Enter, it does not block it (both branches).** `KeyPressed` fires as Enter reaches Claude Desktop. Claude sends the prompt and clears the box first. So the clipboard read copies an empty box, and any model switch applies to the next message. Fix: set `e.SuppressEvent = true` in the handler when Claude is the foreground window, do the work, then re-send Enter.

**D2. Clipboard read has no foreground check (both branches).** The integration branch checks the foreground window before the keyboard model selector, but not before `ClipboardInputReader` sends Ctrl+A and Ctrl+C. The hook fires on Enter in every app. Once D3 is fixed, pressing Enter in a terminal would send Ctrl+C into it and kill the running process. Move the foreground check to the first line of the hook handler, so nothing runs unless Claude is in front.

**D3. Clipboard runs on an MTA thread (both branches).** The integration branch moved the work to `Task.Run`, which is right for the hook, but thread-pool threads are MTA. WinForms `Clipboard` needs STA and throws ThreadStateException. The catch returns "", so today the read most likely always fails silently. Fix: run the read on the WPF dispatcher (`Application.Current.Dispatcher.Invoke`) or a dedicated STA thread.

**D4. The selector re-triggers the hook (both branches).** The keyboard selector ends with `SendKeys.SendWait("{ENTER}")`. The global hook sees injected keys. On the integration branch the throttle is 500 ms, but the selector takes about 1.1 s (5 Tabs x 100 ms, then 200, 100, 300), so its Enter arrives after the throttle window and starts a second cycle. Fix: a busy flag set before the work and cleared after, and ignore injected events (SharpHook exposes whether an event was simulated).

**D5. Foreground check matches too much (integration branch).** It accepts any window whose title contains "Claude". That includes a browser tab on claude.ai, this chat, or a text file named Claude-notes. Check the process name (`GetWindowThreadProcessId` then `Process.GetProcessById(...).ProcessName == "claude"`) instead of the title.

**D6. Keyboard selection is blind (both branches).** Fixed 5 Tabs, Space, one arrow, Enter, then returns true regardless. Tab count and dropdown order are unverified guesses. Failure is never detected, so the popup fallback never runs. Up/Down only works for two models in a fixed order.

## 4. Other issues

- **Admin warning is based on a wrong premise (integration branch).** Low-level keyboard hooks do not need admin. The real limit: a non-elevated app cannot see keys sent to an elevated window. Claude Desktop runs non-elevated, so every normal user gets a MessageBox on every start for nothing. Remove it, or only warn when the foreground Claude window is elevated.
- **Popup choice goes nowhere (both).** `GetSelectedModel()` is never called. No timeout or auto-accept.
- **Only Haiku and Sonnet (both).** No Opus.
- **Keyword substring matches (both).** "refactor" contains "fact" (Haiku keyword), "list" matches "checklist". Use `\bword\b`.
- **Confidence hits 100 % on one-sided signals (both).** "list my files" normalises to 1.0 and takes the auto-select path.
- **Sanitizer gaps (integration branch).** `sk-\w{20,}` misses Anthropic keys, because `sk-ant-api03-...` has hyphens. The generic pattern catches the tail, but the prefix still logs. Safer default: log no prompt text at all, only length, tokens, model, confidence.
- **Prompt preview on disk (`sonnet-build-fixes`).** Logs 50 raw chars per prompt, no sanitizer.
- Tokenizer rebuilt on every Enter, `_pickLog` has no lock, log cleanup uses file creation time, FileSystemWatcher needs a debounce.

## 5. Order of work

1. Base on `sonnet-fix-and-wire-integration`, apply B1 to B3 and S1. Goal: builds and stays running with a tray icon. **About 30 minutes.**
2. D2 + D5: one process-based foreground check as the first line of the hook handler. D4: busy flag + ignore simulated events. These make it safe before it works. **About 30 minutes.**
3. D1 + D3: suppress Enter, read on an STA thread, re-send Enter. Test on real Claude Desktop. **This is the go/no-go gate.** If Claude's Electron input does not accept a re-sent Enter cleanly, switch the trigger to a hotkey (for example Ctrl+Shift+Enter) instead of hijacking Enter. **1 to 2 hours plus testing.**
4. D6: measure the real Tab count, add Opus, or drop auto-select and always show the popup. **1 to 2 hours.**
5. Section 4. **About 1 to 2 hours.**
