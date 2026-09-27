# OPUS HANDOFF — Architecture Review & Ship Decision

## Pre-Read
1. Read `SECURITY-AUDIT.md` (findings section)
2. Read `HANDOFF-SONNET.md` (what Sonnet fixed)
3. Read Sonnet's updated code (security mitigations should be in place)

---

## Your Task: Architecture Review + Ship/No-Ship Decision

**Time estimate:** 2-4 hours  
**Deliverable:** `OPUS-REVIEW.md` (architecture assessment) + ship decision

### What Sonnet Did
- Fixed build issues (dependencies, XAML syntax)
- Replaced FlaUI with clipboard + keyboard navigation
- Added structured file logging
- Added JSON config system
- Refactored into smaller services
- Added unit test templates
- Implemented security mitigations (prompt sanitization, focus verification)

### Your Job
Review the **integrated app** holistically:
1. Is architecture sound for production?
2. Are security mitigations complete?
3. Are there design flaws or missed edge cases?
4. Is this ready to ship or does it need work?

---

## Architecture Review Checklist

### Layers & Responsibilities

- [ ] **Keyboard Hook Layer** (KeyboardHookService)
  - Intercepts Enter, doesn't block other apps
  - Throttling prevents rapid-fire events
  - Thread-safe (uses lock or concurrent collection)

- [ ] **Input Reading Layer** (ClipboardInputReader, fallback to FlaUI)
  - Tries clipboard first, falls back gracefully
  - Handles empty input correctly (no hang or crash)
  - Restores clipboard state (or config skips it)

- [ ] **Analysis Layer** (PromptAnalyzer)
  - Token counting works (or gracefully estimates)
  - Keywords loaded from config (not hardcoded)
  - Confidence scoring is deterministic
  - Edge cases handled (empty, huge, special chars)

- [ ] **Model Selection Layer** (KeyboardModelSelector, fallback to FlaUI)
  - Verifies focus before sending keys
  - Retries on failure (keyboard nav, then FlaUI, then popup)
  - Times out gracefully (doesn't hang forever)
  - Logs which method succeeded (for debugging)

- [ ] **UI Layer** (ModelPickDialog, tray icon)
  - Popup shows recommendation clearly
  - Auto-accept on high confidence (configurable)
  - Timeout resets if user interacts (they control the flow)
  - Dismiss doesn't break subsequent picks

- [ ] **Persistence Layer** (FileLogger, Config)
  - Logs to dated files with rotation
  - Config hot-reloads on save
  - No crashes on missing config (uses defaults)
  - Prompts sanitized before logging

### Dependency Injection & Testability

- [ ] Services accept dependencies via constructor
- [ ] No static dependencies (hard to mock/test)
- [ ] Logging passed to services (not hardcoded Debug.WriteLine)
- [ ] Tests can swap real services for mocks

### Error Handling

- [ ] Every async/hook operation has try-catch
- [ ] Errors logged with context (what failed, why)
- [ ] Graceful fallbacks (clipboard → FlaUI → popup)
- [ ] User-facing errors are clear (not dev jargon)
- [ ] No silent failures (at least Debug.WriteLine)

### Performance & Threading

- [ ] Keyboard hook doesn't block Claude Desktop
- [ ] Input reading doesn't freeze UI (<100ms)
- [ ] Analysis completes quickly (<500ms)
- [ ] FileLogger is async or uses background thread
- [ ] ConfigManager watch doesn't spam reloads

### Configuration

- [ ] config.json has all tuneable values
- [ ] Defaults are sensible
- [ ] Secrets (if any) NOT in config.json
- [ ] Config keys match code (no typos)
- [ ] Examples provided (config.json is self-documenting)

---

## Security Review (From SECURITY-AUDIT.md)

### Critical (Fix before ship)
- [ ] **Credential Leakage:** Prompts are sanitized (no API keys logged)
  - Verify: `PromptAnalyzer.SanitizePrompt()` removes patterns
  - Test: Log a prompt with "password=abc123", verify it's redacted
  
- [ ] **Focus Verification:** Keyboard navigation checks Claude is focused
  - Verify: `KeyboardModelSelector.SelectModelViaKeyboard()` calls `VerifyFocus()`
  - Test: Click email client, run selector, verify it doesn't type there

### Important (Fix if time)
- [ ] **Admin Privilege Warning:** App warns if running without admin
  - Verify: Startup code checks `IsAdmin()` and shows MessageBox
  - Test: Run as user, see warning; run as admin, proceed
  
- [ ] **Clipboard Restoration:** Logic is safe (doesn't corrupt)
  - Verify: `ClipboardInputReader.ReadPromptViaClipboard()` handles exceptions
  - Test: Have something in clipboard, call read, verify clipboard unchanged

- [ ] **Config Validation:** Invalid config doesn't crash app
  - Verify: `ConfigManager.Load()` has ValidateConfig() with try-catch
  - Test: Edit config.json with invalid JSON, run app

### Nice to Have
- [ ] **Dependency Audit:** NuGet packages are up-to-date and maintained
  - Verify: Run `dotnet outdated` (or manual check) for vulnerabilities
  - Note: `tiktoken-net` is third-party (estimate, not official)

---

## Ship Decision

### Ship if:
- ✅ Builds without errors
- ✅ Compiles to .exe (no runtime errors)
- ✅ Keyboard hook fires and reads prompt (or gracefully falls back)
- ✅ Model selection works (keyboard nav or FlaUI)
- ✅ Security P1 mitigations in place (sanitized logs, focus check)
- ✅ Tested on real Claude Desktop (not just compilation)
- ✅ Error handling is robust (no crashes on edge cases)
- ✅ Documentation is clear (user can install & run)

### No-Ship / Needs Work if:
- ❌ Hook never fires (keyboard input not intercepted)
- ❌ Input reading fails consistently (clipboard + FlaUI both broken)
- ❌ Credentials leaked in logs (sanitization missing or broken)
- ❌ Crashes on unicode/long prompts (crashes, not fallback)
- ❌ UI is confusing (user doesn't understand model pick)
- ❌ No way to disable/uninstall cleanly
- ❌ Admin requirement not documented

---

## Final Checklist Before Shipping

- [ ] All P1 security issues fixed
- [ ] Build passes clean
- [ ] Tested on real Claude Desktop
- [ ] Logs created and sanitized
- [ ] Config works and validates
- [ ] Tests pass
- [ ] Documentation complete
- [ ] README updated with security notes
- [ ] Version number bumped (e.g., 0.1.0)

