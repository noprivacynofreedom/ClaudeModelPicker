# OPUS REVIEW — ClaudeModelPicker

Reviewed: 2026-09-28, commit `84ff2a5` (main)
Method: static read of every source file. No build: the review sandbox cannot download the .NET SDK, so every "will not build" item below comes from reading the code against the package APIs. Confirm each one with `dotnet build` on Windows.

## Verdict: NO-SHIP

The Sonnet pass described in HANDOFF-OPUS.md did not land. The pre-conditions for this review are not met:

| Promised by Sonnet | Status in repo |
|---|---|
| ClipboardInputReader | Missing |
| KeyboardModelSelector + VerifyFocus() | Missing |
| PromptAnalyzer.SanitizePrompt() | Missing |
| IsAdmin() startup check | Missing |
| ConfigManager | File exists, never constructed |
| FileLogger | File exists, never constructed |
| ClaudeMonitorService split up | Not done, still the God object |
| Unit tests | File exists, breaks the build (see B3) |

So the app today is the first-pass code plus two unwired services. It does not build, and if it built it would crash on start. The bigger problem is a design flaw in the core flow (D1) that no amount of polish fixes.

---

## 1. Build blockers (fix first, about 30 minutes)

**B1. Wrong tokenizer package.** `PromptAnalyzer.cs` uses `using SharpToken;` and `GptEncoding`. The csproj references `tiktoken-net`, which has neither. Fix: swap the PackageReference to `SharpToken` (or rewrite against tiktoken-net), then delete the unused one.

**B2. SharpHook key name.** `KeyCode.Return` does not exist in SharpHook 5.x. The Enter key is `KeyCode.VcEnter`.

**B3. Tests compile into the app.** `Tests/PromptAnalyzerTests.cs` sits inside the project folder, so the SDK globs it into the WPF app. The app has no xUnit reference, so `using Xunit;` fails. Fix: move tests to a separate `ClaudeModelPicker.Tests` project, or add `<Compile Remove="Tests\**" />` to the app csproj.

**B4. XAML `Spacing`.** `ModelPickDialog.xaml` sets `Spacing="10"` on a StackPanel. WPF StackPanel has no Spacing property (that is WinUI / Avalonia). Use `Margin` on the buttons.

**B5. Deprecated SDK.** `Microsoft.NET.Sdk.WindowsDesktop` still works with a warning. Change to `Microsoft.NET.Sdk` (UseWPF is already set). Also remove `System.Management`, nothing uses it.

## 2. Crash on start

**C1. `MainWindow` is null in OnStartup.** App.xaml sets `StartupUri`, and WPF creates that window after `OnStartup` returns. `MainWindow.Hide()` on line 16 throws NullReferenceException outside the try block. Fix: remove `StartupUri` and remove MainWindow entirely. A tray app needs no window, use `ShutdownMode="OnExplicitShutdown"`.

**C2. Popup on the wrong thread.** SharpHook raises `KeyPressed` on its own hook thread. `ShowPopup` builds a WPF Window there, which throws "The calling thread must be STA". The catch swallows it, so the popup silently never shows. Fix: `Application.Current.Dispatcher.InvokeAsync(...)`.

## 3. Design flaws (the real no-ship reasons)

**D1. The hook observes Enter, it does not intercept it.** This is the big one. `KeyPressed` fires as Enter goes to Claude Desktop. By the time the app reads the input field, Claude has already sent the prompt and cleared the box. So:
- the read returns empty (or the next draft), and
- any model switch applies to the *next* message, not this one.

The whole premise needs this order: block Enter, read, decide, switch, then re-send Enter. SharpHook supports that: set `e.SuppressEvent = true` in the handler (Windows only), do the work off the hook thread, then send Enter with SharpHook's `EventSimulator`. The suppress decision must be instant, so only suppress when Claude Desktop is the foreground window (see D2) and a pick is not already in flight.

**D2. No foreground check.** The hook fires on Enter in every app on the PC. It then finds the Claude window by name and reads and clicks it, even while you type in Discord or a terminal. Add a `GetForegroundWindow()` + process-name check (`claude.exe`) as the first line of the handler. This is also the "focus verification" P1 from the security audit.

**D3. Blocking work on the hook thread.** FlaUI tree walks plus `Thread.Sleep(300)` run inside the hook callback. Windows drops low-level hooks that take longer than `LowLevelHooksTimeout` (about 1 s on Windows 10/11), and can silently unhook the app. Keep the callback to "check foreground, suppress, enqueue" and nothing else.

**D4. Popup choice goes nowhere.** `ModelPickDialog.GetSelectedModel()` is never called. Clicking "Use Sonnet" does nothing. No timeout or auto-accept either, even though config defines both.

**D5. Only two models.** Picks are Haiku or Sonnet. The app never picks Opus, and model names are string literals in five places. Use one enum or one list from config.

## 4. Analysis logic bugs

**A1. Substring keyword matches.** `Contains` hits inside other words:
- "refactor" contains "fact" (a Haiku keyword), so a refactor request scores for Haiku too.
- "list" matches "checklist", "specialist", "listen".

Use whole-word regex: `\bfact\b`.

**A2. Confidence goes to 100 % on one signal.** Scores are normalised by their own total. A prompt with only Haiku signals ("list my files") gets haiku 0.8 / total 0.8 = 1.0, which is "100 % confidence" and triggers the auto-click path (> 0.8). Confidence should reflect evidence strength, not the ratio. Simple fix: `confidence = 0.5 + (winner - loser) / 2`, capped, with weights from config.

**A3. Config is ignored.** Thresholds, keywords, weights and the 0.8 auto-click cutoff are all hardcoded. The config says 0.85 and a Sonnet keyword weight of 0.5. The code uses 0.8 and 0.4.

**A4. Wrong tokenizer and rebuilt every call.** cl100k_base is OpenAI's encoding, not Claude's, so counts are an estimate anyway. The encoding is also re-created on each Enter press. For a 150/300 token threshold, `text.Length / 4` is accurate enough and drops a dependency. If you keep a tokenizer, cache it in a static field.

## 5. Security audit status

| Finding | Priority | Status |
|---|---|---|
| Prompt text in logs, no redaction | P1 | **Open.** `LogModelPick` keeps the first 100 chars raw. `FileLogger.LogAnalysis` would write 50 chars raw to disk once wired. |
| Focus verification before sending keys | P1 | **Open.** See D2. |
| Admin warning | P2 | **Audit is wrong here.** `WH_KEYBOARD_LL` hooks do not need admin. The real limit: a non-admin app cannot see keys sent to elevated windows. Claude Desktop runs non-elevated, so no admin needed. Fix the README and audit, do not add an admin prompt. |
| Clipboard restore | P2 | N/A until the clipboard reader exists. |
| Config validation | P2 | **Partial.** Missing file throws in the constructor (handoff says use defaults). Bad JSON on hot-reload keeps the old config, which is correct. |
| Dependency audit | P3 | Two unused packages (B1, B5). |

Sanitization note for whoever builds it: redaction regexes miss things. The safer default is to log no prompt text at all, only length, token count, picked model and confidence. Add a `logPromptPreview: false` config flag for debugging.

## 6. Smaller issues

- `_pickLog` is a List written from the hook thread with no lock.
- `FileLogger.CleanupOldLogs` uses file creation time. Parse the date from the file name instead, creation time changes on copy.
- `FileSystemWatcher.Changed` fires 2 to 3 times per save. Debounce it.
- Claude window found by exact name "Claude". Match by process instead.
- Repo hygiene: `ClaudeModelPicker.zip` is a stale copy of the old scratchpad and `PUSH-TO-GITHUB.md` is a one-time note. Delete both. Add a `.gitignore` for `bin/` and `obj/`.
- No tray icon exists, so there is no visible way to exit the app. The ship list requires "a way to disable/uninstall cleanly".

## 7. Recommended order of work

1. Fix B1 to B5 and C1. Goal: clean `dotnet build`, app starts and stays running. **About 30 minutes.**
2. Tray icon with Enable/Disable and Exit. **About 30 minutes.**
3. Rebuild the hook flow for D1 to D3: foreground check, suppress Enter, queue to a worker, re-send Enter. Test this on real Claude Desktop before anything else, because if the Enter suppress/re-send does not work cleanly with Claude's Electron input, the product idea needs a different trigger (for example a hotkey like Ctrl+Shift+Enter). **1 to 2 hours plus testing.**
4. Clipboard reader and keyboard-nav selector from RESEARCH-InputMethods.md. **1 to 2 hours.**
5. Wire ConfigManager and FileLogger, fix A1 to A3, log no prompt text. **About 1 hour.**
6. Move tests to their own project and add tests for A1 and A2. **About 30 minutes.**

Step 3 is the go/no-go gate for the whole project. Do it before investing in steps 4 to 6.
