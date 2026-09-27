# Opus Review — ClaudeModelPicker

Fill this in after Sonnet finishes Priorities 1–3 in HANDOFF-SONNET.md.
Sections below are scaffolding; replace `[ ]` with `[x]` and add notes as
each is checked.

## 1. Architecture review

- [ ] `ClaudeMonitorService` split cleanly into `InputReader` /
      `ModelSelector` / `Logger` (or equivalent), no God-object left
- [ ] Keyboard hook (`KeyboardHookService`) never blocks the global hook
      thread — verify all analysis/click work moved to `Task.Run()`
- [ ] Config (`ConfigManager` / `config.json`) is the single source of
      truth for thresholds/keywords — no hard-coded duplicates remain in
      `PromptAnalyzer` or `KeyboardHookService`
- [ ] Fallback chain is explicit and testable: auto-click (FlaUI or
      keyboard nav) → popup, never silent no-op
- [ ] `IDisposable` used correctly throughout (UIA3Automation, hook,
      logger) — no resource leaks on app exit

## 2. Security findings (from docs/security-audit.md)

- [ ] Prompt text sanitized (`FileLogger.Sanitize()` or equivalent) before
      anything touches disk
- [ ] README documents the keyboard hook and clipboard use plainly —
      first paragraph, not buried
- [ ] `tiktoken-net` / `SharpToken` dependency mismatch resolved in
      `.csproj`
- [ ] No new network calls introduced by Sonnet's changes (dashboard,
      settings sync, etc.) without a matching audit note added here

## 3. Functional verification (needs a Windows machine with Claude Desktop)

- [ ] Keyboard hook fires on Enter inside Claude Desktop
- [ ] `ReadClaudeInputField()` (or its clipboard-based replacement)
      actually captures the typed prompt
- [ ] Popup shows with a sensible model recommendation
- [ ] Auto-click either works, or degrades to popup without error
- [ ] Throttle prevents rapid-Enter crash/spam (Priority 3)
- [ ] App survives Claude Desktop not running (no exception, no hang)
- [ ] App survives empty input field

## 4. Test coverage

- [ ] `PromptAnalyzerTests.cs` templates (cloud-handoff/Tests/) filled in
      and passing
- [ ] Test project (`ClaudeModelPicker.Tests.csproj`) added and wired into
      `dotnet test`
- [ ] Stress test for rapid Enter presses run and passing

## 5. Open questions for Jason

- Auto-click default: ship `autoClickEnabled: false` (current config
  default) until keyboard-nav tab-stop counts are confirmed against real
  Claude Desktop, or flip it on once Sonnet verifies?
- Tray icon / settings UI / auto-start on boot (HANDOFF-SONNET.md
  "Missing Features") — in scope for this pass, or a v2?

## 6. Ship checklist

- [ ] `dotnet build -c Release` succeeds clean
- [ ] `dotnet test` succeeds clean
- [ ] README updated (keyboard hook disclosure, setup steps, config.json
      docs)
- [ ] Version tagged / release notes drafted
