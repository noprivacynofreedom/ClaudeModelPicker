# Handoff: ClaudeModelPicker v0.2, real model switching in Claude Desktop

## Purpose
Make the app actually switch Claude Desktop's model before the message sends. Today it reads the prompt, recommends a model, and sends, but never changes the model.

## Current state
- Branch `sonnet-fix-and-wire-integration`, tip `060af4c`. Only branch to work on.
- v0.1 tested on Jason's PC, all passing:
  - Hook only acts on plain Enter when process `claude` is foreground (`Services/ForegroundCheck.cs`). Shift/Ctrl/Alt/Win+Enter pass through.
  - Enter is suppressed (`e.SuppressEvent`), prompt read via Ctrl+A/Ctrl+C on a dedicated STA thread, then `{RIGHT}{ENTER}` re-sent. Busy flag + `IsEventSimulated` stop loops.
  - Clipboard read waits on `GetClipboardSequenceNumber`; returns "" if Ctrl+C copied nothing.
  - Popup only shows when the recommendation differs from `_lastModel`. Button choice returned and passed to `InputMethodManager.SelectModel`, logged as MODEL_CHOSEN.
  - After popup closes: wait for Claude foreground (up to 1 s) + 250 ms before re-sending Enter (without it, Enter was lost intermittently).
- Model selection currently does NOTHING:
  - Keyboard Tab walk is disabled in `config.json` (`modelSelection.methods.keyboard.enabled: false`). On real Claude it tabbed across the UI instead of opening the model menu. Dead approach.
  - FlaUI path is disabled in config and its code (`Services/ClaudeMonitorService.cs`, `TryClickModelDropdownViaFlaUI`) was never verified against the current Claude Desktop UI.
- Claude Desktop UI (from a screenshot): model picker sits bottom-right of the prompt box, button text like "Opus 5.5" next to an effort label "Medium".
- Analyzer only knows "Haiku" and "Sonnet". Current Claude models: Opus 5.5, Sonnet 5, Haiku 4.5, Fable 5.1.
- Branch `sonnet-build-fixes` is redundant. Jason deletes it himself (cloud session's branch delete was blocked).

## Relevant files / links
- Repo: https://github.com/noprivacynofreedom/ClaudeModelPicker
- Local: `F:\APP-DEV\projects\ClaudeModelPicker`
- `Services/KeyboardHookService.cs`, `Services/InputMethodManager.cs`, `Services/ClaudeMonitorService.cs`, `Services/ConfigManager.cs`, `Services/PromptAnalyzer.cs`, `ModelPickDialog.xaml(.cs)`, `config.json`
- Log: `%AppData%\ClaudeModelPicker\logs\<yyyy-MM-dd>.log`

## Constraints
- Cloud session cannot build .NET. Jason builds and tests. Every command gets the full path, e.g. `dotnet build F:\APP-DEV\projects\ClaudeModelPicker\ClaudeModelPicker.csproj`. One command block per turn.
- Do not drive his PC. Ask him to run and paste results.
- Before pushing C#, check every API against code that already compiles on his PC, or against the package source (FlaUI 4.0.0, SharpHook 5.3.0 on GitHub). No guessed APIs.
- Every file lists its own usings. Implicit usings stay off.
- No synthetic Tab/arrow keystrokes for selection. UI Automation Invoke/Select only, scoped to the `claude` process window.
- After any selection, focus must return to the prompt box before Enter is re-sent (UIA `Focus()` on the input element), or the message will not send.
- He is low on usage: small commits, short replies.

## Expected output
1. `ConfigManager.KeyboardEnabled` default flipped to `false` (safety: a missing config.json currently re-enables the Tab walk).
2. A UIA diagnostic Jason can run once that logs Claude Desktop's model button name and menu item names, so selectors are built from real names, not guesses.
3. `SelectModel` via UIA: open the model button, invoke the menu item matching the target model, refocus the prompt box. Wired into `InputMethodManager`, FlaUI enabled in config.
4. Test on his PC: send a prompt, click a popup button, check Claude's model label changed before the message sent.
5. Optional, flag as scope creep first: add Opus and full current model names to the analyzer.

## Suggested approach
Start with `/context-pickup`. Do item 1 + the diagnostic (item 2) in one commit, have Jason run it and paste the element names, then build item 3 from those names.
