# Security Audit — ClaudeModelPicker

Reviewed: ClaudeMonitorService.cs, KeyboardHookService.cs, PromptAnalyzer.cs,
ModelPick.cs, ModelPickLog.cs, ClaudeModelPicker.csproj.

## 1. Credential leakage in logs

**Finding — real risk.** `ClaudeMonitorService.LogModelPick()` stores
`prompt.Substring(0, Math.Min(100, prompt.Length))` in memory
(`_pickLog`), and this history has no size cap beyond 1000 entries — it's
never written to disk in the current code, but Task 3 / HANDOFF-SONNET.md
Priority 3 asks for file logging, which will persist this. If a user
pastes an API key, password, or token as part of a prompt (easy to do by
accident — pasting a config file, an .env snippet, a curl command), the
first 100 characters could contain it.

**Fix:** `FileLogger.Sanitize()` (Task 2, already implemented) regex-strips
`sk-...` and `Bearer ...` patterns before writing to disk. This is a
best-effort pattern match, not a guarantee — it won't catch every secret
shape (AWS keys, generic passwords, etc.). Recommend the log line documents
this limitation explicitly rather than implying prompts are fully safe to
log.

**Also flag:** the global keyboard hook (`SimpleGlobalHook`) fires on every
Enter key system-wide, not just inside Claude Desktop.
`ReadClaudeInputField()` only returns something if the Claude window is
focused, so in practice non-Claude Enter presses should return empty
string and short-circuit — but this depends on the FlaUI window match
working, which is unverified (see docs/clipboard-keyboard-nav.md). If the
window match is wrong, the hook could attempt to read/log keystrokes from
an unrelated foreground app. Worth an explicit unit/integration test once
Sonnet has a Windows box to test on.

## 2. Prompt sanitization

**Finding.** No sanitization currently exists anywhere in the pipeline —
prompt text flows from `ReadClaudeInputField()` straight into
`AnalyzePrompt()` and `LogModelPick()` unmodified. Covered by the
`FileLogger.Sanitize()` fix above for the logging path. The in-memory
`_pickLog` (still truncated to 100 chars, unsanitized) is lower risk since
it's process-memory-only and cleared on exit, but if Sonnet adds a "usage
dashboard" (HANDOFF-SONNET.md Priority 3 / Missing Features) that reads
`_pickLog`, sanitize there too before it's ever rendered to screen or
exported.

## 3. Admin privilege requirements

**Finding — needs documenting, not fixing.** Global low-level keyboard
hooks on Windows (what `SharpHook.SimpleGlobalHook` installs) do not
strictly require admin rights to *install*, but:
- They **do** require the hooking process to run at the same or higher
  integrity level as the target window to intercept its keystrokes. If
  Claude Desktop (Electron) or Windows itself is running elevated for any
  reason, ClaudeModelPicker would silently fail to intercept unless also
  elevated.
- Antivirus / Windows Defender SmartScreen commonly flags unsigned apps
  that install global keyboard hooks — this is indistinguishable in
  behavior from a keylogger to security software and to a suspicious user.

**Recommendation for README (not yet present):** state plainly that this
app installs a system-wide keyboard hook, name exactly what it reads
(Claude Desktop's input field only, when focused) and what it does with
that data (local analysis + local log file, nothing sent over the
network), and note it may need to be run as the same user session as
Claude Desktop, not necessarily "as Administrator." This is a trust
disclosure issue, not a code fix — put it in the README, first paragraph.

## 4. Other findings (outside the three questions asked, flagged because they're cheap to fix)

- **Dependency mismatch:** `ClaudeModelPicker.csproj` still lists
  `tiktoken-net` as a package reference, but `PromptAnalyzer.cs` already
  imports `SharpToken` (`using SharpToken;`). This looks like a partial
  migration — the .csproj needs `tiktoken-net` removed and
  `SharpToken 2.0.1` added, or the build will either fail to resolve
  `SharpToken` or carry a dead, unused `tiktoken-net` reference. This
  overlaps HANDOFF-SONNET.md Priority 1 (listed as "Fix .csproj:
  tiktoken-net → SharpToken") — flagging here because it also means
  `PromptAnalyzerTests.cs` (Task 4) cannot run until this is fixed.
- **No network calls anywhere in reviewed code** — nothing to audit for
  exfiltration. If a "usage dashboard" or telemetry feature is added
  later, re-audit at that point.
- **Clipboard approach (Task 1) reads/writes the system clipboard.** Not a
  security issue per se, but it's a side-channel: any other app polling
  the clipboard during that ~80ms window could see the user's prompt text
  in plaintext. Low risk, but worth one line in the README next to the
  keyboard-hook disclosure.

## Summary for Opus review

No critical vulnerabilities in the reviewed code (no network I/O, no
credential storage, no elevation of privilege beyond what a keyboard hook
inherently needs). The two real action items are: (1) sanitize prompt text
before it's ever persisted to disk — handled by `FileLogger.Sanitize()` —
and (2) document the keyboard-hook and clipboard behavior plainly in the
README so the app doesn't read as a keylogger to a user or to antivirus
software.
