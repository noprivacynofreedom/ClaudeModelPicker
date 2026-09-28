# OPUS HANDOFF — Architecture Review & Ship Decision

## First thing to do, before reading anything else

**This code has never been built or run.** This session (Sonnet, cloud,
Linux container) has no Windows machine and no .NET SDK — `dotnet build`
was never executed against this project. Every fix below was made by
reading the code and reasoning about it, not by compiling it.

So step 1 is not architecture review — it's: **ask Jason to run
`dotnet build -c Release` on his own machine and paste back whatever
errors come out.** Per his stated preference, don't try to drive his PC to
test this yourself; ask him to run it and report. Everything past this
point assumes that comes back clean or close to it.

---

## What was actually fixed this session (Sonnet)

Starting point: two build blockers (documented in HANDOFF-SONNET.md
Priority 1), and cloud-research artifacts (`Services/ConfigManager.cs`,
`Services/FileLogger.cs`, `config.json`, `Tests/PromptAnalyzerTests.cs`,
`SECURITY-AUDIT.md`, `RESEARCH-InputMethods.md`) that existed as files but
were never wired into the app — `ClaudeMonitorService`,
`KeyboardHookService`, and `PromptAnalyzer` still had the original
hardcoded, FlaUI-only, synchronous code.

### Build blockers
- `.csproj`: `tiktoken-net` → `SharpToken 2.0.1` (code already imported
  `SharpToken`; the package reference never matched)
- `ModelPickDialog.xaml`: `StackPanel.Spacing="10"` removed — that
  property doesn't exist on WPF's `StackPanel` (it's a WinUI/Avalonia
  thing), replaced with per-button `Margin`
- `Services/FileLogger.cs` and `Services/ConfigManager.cs` (added by the
  cloud-research session) were missing `using System.Linq;` /
  `using System.Collections.Generic;` — would not have compiled as
  dropped in
- `.csproj` was missing `<UseWindowsForms>true</UseWindowsForms>` —
  needed for `Clipboard`, `SendKeys`, and `NotifyIcon`, none of which
  work in a plain WPF project without it

### A real ship-blocking bug found while wiring things together
`ConfigManager`'s constructor threw `FileNotFoundException` if
`config.json` wasn't sitting next to the `.exe` at
`AppContext.BaseDirectory` — and the `.csproj` never copied `config.json`
there. On a fresh clone + build, the app would have thrown on line 1 of
`App.xaml.cs`'s startup and shown nothing but an error MessageBox. Fixed
two ways: `.csproj` now copies `config.json` to the output directory, and
`ConfigManager.Load()` falls back to an empty config (→ built-in defaults
via each `Get*()` accessor) instead of throwing, so a missing or corrupt
config.json degrades gracefully instead of killing the app.

### Actually wired the fallback chain (this didn't exist before)
`RESEARCH-InputMethods.md` had sketched `ClipboardInputReader` and
`KeyboardModelSelector` as ideas; neither existed as a file, and nothing
called them. Built:
- `Services/ClipboardInputReader.cs` — Ctrl+A/Ctrl+C/read/restore, with
  clipboard-lock retry
- `Services/KeyboardModelSelector.cs` — Tab/arrow/Enter navigation, **with
  a foreground-window check before sending any keystrokes** (P1 item from
  SECURITY-AUDIT.md "Keyboard nav focus issue" — previously nothing
  verified Claude Desktop was even the focused window before blindly
  sending Tab/Enter/Arrow keys system-wide)
- `Services/InputMethodManager.cs` — coordinates clipboard → FlaUI for
  reading, keyboard-nav → FlaUI for selecting, respecting the
  `enabled`/`disabled` flags already in `config.json`
- `Services/ClaudeMonitorService.cs` — trimmed down to just the FlaUI
  fallback methods (renamed `ReadClaudeInputFieldViaFlaUI` /
  `TryClickModelDropdownViaFlaUI` so it's clear they're the legacy path,
  not the primary one)

### Async + throttle (HANDOFF-SONNET.md Priority 3, previously not done)
`KeyboardHookService` used to do clipboard/UI reads, analysis, and popup
display synchronously on the global hook's own thread — any slowness there
would have frozen keyboard input system-wide. Now: the hook callback does
only a throttle check (`config.json` → `modelSelection.throttleMs`) and
hands off to `Task.Run`; the popup dialog is marshaled back to the UI
thread via `Dispatcher.Invoke` since WPF windows can't show from a
background thread.

### Config actually drives behavior now
`PromptAnalyzer` used hardcoded keyword arrays and thresholds; now reads
them from `ConfigManager` (which reads `config.json`). Also added
`PromptAnalyzer.SanitizePrompt()` (P1 from SECURITY-AUDIT.md finding #1 —
prompt text is now regex-scrubbed for key/token/password-shaped strings
*before* it's ever written to the log file, not after).

### Admin-privilege warning + tray icon
Neither existed. `App.xaml.cs` now checks `WindowsPrincipal.IsInRole(Administrator)`
at startup and shows a one-time MessageBox if not elevated (SECURITY-AUDIT.md
flagged this as "needs documenting" — a non-admin install would otherwise
silently never intercept anything, with no clue why). A minimal
`NotifyIcon` + right-click Exit was added — previously "runs as background
tray app" was asserted in the README but `MainWindow` just hid itself with
no tray presence at all.

### Test file patched to match the new constructor
`Tests/PromptAnalyzerTests.cs` called `new PromptAnalyzer()` — broke the
moment `PromptAnalyzer` started requiring a `ConfigManager`. Fixed to
`new PromptAnalyzer(new ConfigManager())`.

---

## What is still NOT verified (the actual unknowns)

Nothing above has run against a live Claude Desktop. Specifically unknown:

1. **Does the clipboard read even work?** Ctrl+A/Ctrl+C sent via
   `SendKeys` assumes Claude Desktop's input field responds to those
   shortcuts normally. Unverified.
2. **Tab count to reach the model selector** — `config.json`'s
   `modelSelection.methods.keyboard.tabCount: 5` is a guess from
   `RESEARCH-InputMethods.md`, never confirmed against the real UI.
3. **Dropdown order** — `KeyboardModelSelector` assumes Down = Sonnet,
   Up = Haiku from whatever the current selection is. Also a guess.
4. **Whether the global hook fires at all inside Claude Desktop**,
   and whether `WarnIfNotAdmin`'s admin check is even the right
   diagnosis if it doesn't.
5. **Whether `SharpToken`'s `cl100k_base` encoding is a reasonable proxy
   for Claude's actual tokenizer** — noted in the original README, still
   unverified either way.

## One loose end, not fixed (flagging, not deciding)

`Models/ModelPickLog.cs` is now orphaned — nothing constructs it anymore.
The original `ClaudeMonitorService` kept an in-memory `List<ModelPickLog>`
for a future "usage dashboard" feature; that responsibility moved to
`FileLogger`'s flat log files instead, and nothing reads them back into a
structured list. Either delete `ModelPickLog.cs`, or if the usage
dashboard is still wanted, build a reader over `FileLogger`'s log
directory. Left as-is rather than guessing which Jason wants.

---

## Your job, Opus

1. Confirm Jason's build report is clean (or work through whatever errors
   come back — there may be more; this was fixed by reading, not
   compiling).
2. Once it builds: everything in "still NOT verified" above needs a real
   Claude Desktop session to check. None of it can be confirmed from
   source reading alone.
3. Sign off or don't. Given point 3 above (nothing has run), "ship" here
   should mean "ship for Jason to test," not "ship to end users."
