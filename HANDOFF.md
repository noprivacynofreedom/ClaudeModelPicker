# Handoff: Make ClaudeModelPicker safe, then working on real Claude Desktop

Supersedes HANDOFF-HAIKU.md, HANDOFF-SONNET.md and HANDOFF-OPUS.md. Those describe earlier stages that are done or replaced. Delete all four handoff files once this task is finished.

## Purpose
Take the app from "builds" to "works on Jason's PC and is safe to leave running". Do review steps 2 to 4 from OPUS-REVIEW.md (on branch `opus-review`), then get a pass/fail from a real test.

## Current state
- Work branch: `sonnet-fix-and-wire-integration`, tip `f162b20`. This is the only branch to work on.
- `dotnet build` passes on Jason's PC: 0 errors, 6 nullable warnings (safe to ignore).
- Done: build fixes, tray icon with Exit, config.json copied to output, ConfigManager falls back to defaults, throttle, hook work moved to Task.Run, popup marshalled to the dispatcher, SanitizePrompt, foreground check inside KeyboardModelSelector only, MainWindow removed (tray-only, OnExplicitShutdown).
- NOT done, and the app must not be run until 1 and 2 are fixed:
  1. **D2 + D5 (safety):** no foreground check before the clipboard read. Ctrl+A / Ctrl+C would fire into any app on Enter (Ctrl+C kills a terminal process). The existing check matches window title "Claude", which also matches a claude.ai browser tab. Fix: one process-based check (GetForegroundWindow, GetWindowThreadProcessId, process name "claude") as the first line of `KeyboardHookService.OnKeyPressed`. Remove the title check.
  2. **D4 (safety):** KeyboardModelSelector's own `{ENTER}` re-triggers the hook after the 500 ms throttle window (selection takes about 1.1 s). Fix: busy flag set before the work, cleared in finally, and ignore simulated events.
  3. **D3:** clipboard work runs on a thread-pool (MTA) thread. WinForms Clipboard needs STA, so the read silently returns "". Run the read on the WPF dispatcher or a dedicated STA thread.
  4. **D1 (go/no-go):** the hook observes Enter after Claude already sent the prompt, so it reads an empty box and the switch lands on the next message. Fix: `e.SuppressEvent = true` when Claude is foreground and not busy, do the work, then re-send Enter. If Claude's Electron input does not accept the re-sent Enter cleanly, switch the trigger to a hotkey (Ctrl+Shift+Enter) and stop hijacking plain Enter.
  5. **Admin MessageBox:** remove `WarnIfNotAdmin` in App.xaml.cs. Low-level hooks do not need admin, and Claude Desktop runs non-elevated.
- Branch `sonnet-build-fixes` is redundant. Delete it once this is done.

## Relevant files / links
- Repo: https://github.com/noprivacynofreedom/ClaudeModelPicker
- Jason's local copy: `F:\APP-DEV\projects\ClaudeModelPicker` (on branch `sonnet-fix-and-wire-integration`)
- Review: `OPUS-REVIEW.md` on branch `opus-review` (sections 3 and 5)
- Code: `Services/KeyboardHookService.cs`, `Services/ClipboardInputReader.cs`, `Services/KeyboardModelSelector.cs`, `Services/InputMethodManager.cs`, `App.xaml.cs`
- Research on input methods: `RESEARCH-InputMethods.md`

## Constraints
- The cloud session cannot build .NET (SDK download is blocked). Jason builds and tests on his PC. Every build/test step goes to him as a PowerShell command with the full path, for example `dotnet build F:\APP-DEV\projects\ClaudeModelPicker\ClaudeModelPicker.csproj`.
- Do not drive his PC to test. Ask him to run it and paste the result.
- Before pushing any C# change, check every API call against code that already compiled on his PC (the FlaUI and SharpHook calls on this branch now do). Guessed APIs caused 3 wasted build rounds.
- Every file must list its own usings. Implicit usings stay off (WPF + WinForms together make Application, MessageBox and Clipboard ambiguous).
- Keep scope to steps 1 to 5 above. Opus model support, keyword/confidence fixes and the rest of review section 4 are a separate task. Flag it if the work starts pulling them in.
- Jason is time-poor and runs several chats at once: one command block per turn, full paths, no long explanations.

## Expected output
- Pushed commits on `sonnet-fix-and-wire-integration` for steps 1 to 5.
- Clean `dotnet build` on Jason's PC.
- One real test on Jason's PC, with this protocol:
  1. Run `F:\APP-DEV\projects\ClaudeModelPicker\bin\Debug\net8.0-windows\win-x64\ClaudeModelPicker.exe`. Tray icon appears, no MessageBox.
  2. Press Enter in Notepad. Nothing happens (foreground check).
  3. Type a prompt in Claude Desktop and press the trigger. The prompt still sends, and the log at `%AppData%\ClaudeModelPicker\logs\<today>.log` shows a CLIPBOARD_READ with a non-zero length.
  4. Exit from the tray icon.
- A clear verdict: works (ship to Jason as v0.1), or D1 failed and the trigger moved to a hotkey.

## Suggested approach
Start with `/context-pickup`. Do steps 1, 2 and 5 in one commit (small, safe), have Jason build, then do 3 and 4 and run the test protocol.
