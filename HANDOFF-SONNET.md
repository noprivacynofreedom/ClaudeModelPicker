# Claude Model Picker — Handoff to Sonnet (Code Cleanup & Fixes)

**What this is:** A first-pass app that works in theory but needs production polish, better error handling, and feature completeness.

**Code review priority (in order):**

### 1. **Critical: UI Automation Robustness** ⚠️
   - `ClaudeMonitorService.ReadClaudeInputField()` uses fragile selectors
   - Claude Desktop may render input as iframe, shadow DOM, or custom control
   - **Task:** Add fallback strategies:
     - Clipboard read (Ctrl+A, Ctrl+C, read clipboard)
     - Alternative: hook into Claude's local database instead
   - Test with real Claude Desktop window
   - Document what actually works

### 2. **Critical: Dropdown Click Automation**
   - `TryClickModelDropdown()` assumes standard button/menu structure
   - Claude Desktop UI is Electron + React; FlaUI selectors may not match
   - **Task:**
     - Verify selectors actually find the model button in real Claude Desktop
     - If not: fallback to keyboard nav (Tab, arrow keys, Enter)
     - Or: use claude-code's local config to auto-switch (if available)

### 3. **Important: Keyboard Hook Async Handling**
   - Hook fires synchronously in global keyboard thread
   - Long operations (UI reads, clicks) can freeze keyboard
   - **Task:**
     - Move all blocking work to background thread
     - Use `Task.Run()` to analyze & click async
     - Ensure hook doesn't block global input

### 4. **Important: Error Handling**
   - Many catch blocks silently fail (just Debug.WriteLine)
   - **Task:**
     - Add structured logging (file-based or event log)
     - User should know if model pick failed
     - Graceful degradation (never crash; worst case: do nothing)

### 5. **Code Quality**
   - Namespace organization: add `Services/`, `Models/`, `UI/` folders
   - `ClaudeMonitorService` is a God object—split into `InputReader`, `ModelSelector`, `Logger`
   - Add XML doc comments on public methods
   - Remove hard-coded strings (thresholds, keywords, paths)
   - Add configuration file (JSON) for heuristic tuning

### 6. **Missing Features** (if time allows)
   - Tray icon + minimize to system tray
   - Settings window (tune heuristics, enable/disable hook)
   - Usage dashboard (integrate `plan-usage-history.json`)
   - Auto-start on Windows boot
   - Keyboard shortcut for manual model pick (Ctrl+Shift+M)

### 7. **Testing**
   - Unit tests for `PromptAnalyzer` (token count, keyword matching)
   - Integration test for FlaUI selectors (may need to mock or record)
   - Stress test: rapid Enter presses—does app hang?

### 8. **Performance**
   - Token counting (tiktoken) may be slow first time
   - Cache tokenizer instance
   - Profile and optimize hot paths

### 9. **Security**
   - Global keyboard hook requires admin—document this
   - Verify no credential leakage in logs
   - Sanitize prompt before storing (remove API keys, tokens)

---

**Deliverable:** Clean, well-commented, tested code ready for Opus final review.

**Red flags to watch:**
- ❌ Silent failures (always log)
- ❌ Blocking operations in hook
- ❌ Hard-coded UI selectors (make configurable)
- ❌ No error recovery (always have a fallback)
