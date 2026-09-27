# Security Audit: ClaudeModelPicker

**Date:** September 27, 2026  
**Scope:** Complete codebase review for credential leakage, prompt injection, privilege escalation, and data handling.  
**Status:** Initial audit (needs testing on real Claude Desktop)

---

## Executive Summary

**Risk Level:** LOW-MEDIUM

The app intercepts user input and logs model picks, creating potential vectors for:
1. **Credential leakage in logs** (users may paste API keys in prompts)
2. **Clipboard side effects** (reading/restoring user clipboard)
3. **Privilege escalation** (keyboard hook requires admin)
4. **Prompt injection** (if external config loaded insecurely)

**Recommendations:** All findings are mitigable with existing design. See below.

---

## 1. Credential Leakage in Logs

### Finding: Prompts Logged to Disk

**File:** `ClaudeMonitorService.cs:120-124`  
**Code:**
```csharp
_pickLog.Add(new ModelPickLog {
    Timestamp = DateTime.Now,
    Prompt = prompt.Substring(0, Math.Min(100, prompt.Length)), // <-- ISSUE
    PickedModel = pick.PickedModel,
    Confidence = pick.Confidence
});
```

**Risk:** User prompts are stored in memory and via `FileLogger` to disk (`%AppData%\ClaudeModelPicker\logs\`).  
If a user asks Claude for API key help or pastes a token, it gets logged.

**Severity:** MEDIUM  
**Likelihood:** HIGH (users paste credentials in AI chats)  
**Impact:** Local credential leak if logs accessed

### Mitigation (For Sonnet)

**Option 1: Hash prompts instead of storing text**
```csharp
using System.Security.Cryptography;

string HashPrompt(string prompt) =>
    Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(prompt)));
    
// Log
(_pickLog.Add(new ModelPickLog {
    Prompt = HashPrompt(prompt), // Store hash, not plaintext
    // ... rest
});
```

**Option 2: Sanitize before logging** (recommended)
```csharp
string SanitizePrompt(string prompt)
{
    // Remove common credential patterns
    var patterns = new[] {
        @"sk-\w{48}",          // OpenAI keys
        @"[A-Z0-9]{20,}",      // Generic API keys
        @"password\s*[:=]\s*\S+",
        @"token\s*[:=]\s*\S+",
        @"secret\s*[:=]\s*\S+"
    };
    
    var sanitized = prompt;
    foreach (var pattern in patterns)
        sanitized = Regex.Replace(sanitized, pattern, "[REDACTED]", RegexOptions.IgnoreCase);
    
    return sanitized;
}

// Log
_pickLog.Add(new ModelPickLog {
    Prompt = SanitizePrompt(prompt).Substring(0, 100),
    // ... rest
});
```

**Option 3: Don't log prompts at all**
```csharp
_pickLog.Add(new ModelPickLog {
    // Prompt = prompt,  // <-- Remove this line
    PickedModel = pick.PickedModel,
    Confidence = pick.Confidence,
    // Add:
    PromptLength = prompt.Length,  // Log only length, not content
});
```

**Recommendation:** Use **Option 2 (sanitize)** for debugging value while reducing risk.  
Add config flag to disable prompt logging entirely: `logging.logPromptText = false`

---

## 2. Clipboard Interference

### Finding: Clipboard Read/Restore Logic

**File:** `RESEARCH-InputMethods.md` (proposed)  
**Code:**
```csharp
string savedClipboard = Clipboard.GetText();
SendKeys.SendWait("^a");
SendKeys.SendWait("^c");
string prompt = Clipboard.GetText();
if (savedClipboard != null)
    Clipboard.SetText(savedClipboard);  // Restore
```

**Risk:**  
1. Race condition: User's clipboard changes between save and restore
2. Clipboard content exposed to app memory (low risk, but present)
3. If restore fails, user loses clipboard content

**Severity:** LOW  
**Likelihood:** MEDIUM (timing-dependent)  
**Impact:** User loses clipboard, minor annoyance

### Mitigation (For Sonnet)

**Option 1: Don't restore clipboard**
- Simpler, less fragile
- User can just re-copy if needed
- Trade: Convenience for robustness

**Option 2: Restore with try-catch and logging**
```csharp
try
{
    if (savedClipboard != null)
        Clipboard.SetText(savedClipboard);
}
catch (Exception ex)
{
    _logger.LogError("Clipboard restore failed", ex);
    // Notify user via popup?
}
```

**Option 3: Don't read via clipboard, use FlaUI fallback**
- Avoids clipboard manipulation entirely
- But FlaUI may not work on Electron (why we're adding clipboard in first place)

**Recommendation:** Use **Option 1** (don't restore). If user cares, set config `clipboard.restoreClipboard = false`.

---

## 3. Keyboard Hook Privilege Requirements

### Finding: Global Keyboard Hook Requires Admin

**File:** `KeyboardHookService.cs` (using SharpHook)  
**Risk:** Global keyboard hooks need admin privileges on Windows.

**Severity:** LOW  
**Likelihood:** HIGH (hook won't fire without admin)  
**Impact:** App won't work unless user runs as admin (or grants permission)

### Mitigation (For Sonnet/Documentation)

**Document clearly:**
1. App requires admin to run (keyboard hook limitation)
2. Add UAC elevation on startup
3. Show warning if hook registration fails

**Code in `App.xaml.cs`:**
```csharp
// Check if running as admin
bool IsAdmin() =>
    new WindowsPrincipal(WindowsIdentity.GetCurrent())
        .IsInRole(WindowsBuiltInRole.Administrator);

if (!IsAdmin())
{
    MessageBox.Show("ClaudeModelPicker requires admin privileges to intercept keyboard. " +
                    "Please run as administrator.",
                    "Admin Required", MessageBoxButton.OK, MessageBoxImage.Warning);
    Application.Current.Shutdown();
}
```

**Recommendation:** Add this check to `MainWindow.xaml.cs` or `App.xaml.cs` startup.

---

## 4. Config File Injection

### Finding: External JSON Config File

**File:** `config.json` (proposed)  
**Risk:** If config.json is in a world-writable location, attacker could modify it.

**Severity:** LOW  
**Likelihood:** LOW (config is in AppData, user-only)  
**Impact:** Attacker could change thresholds, disable logging, modify keywords

### Mitigation (For Sonnet)

**Option 1: Validate config on load**
```csharp
public void Load()
{
    var json = File.ReadAllText(_configPath);
    _config = JObject.Parse(json);
    
    // Validate: ensure no malicious keywords array
    ValidateConfig();
}

private void ValidateConfig()
{
    // Check keywords are reasonable
    var keywords = AnalysisHaikuKeywords;
    if (keywords.Any(k => k.Length > 100))
        throw new InvalidOperationException("Keyword too long");
        
    // Check thresholds are sane
    if (AnalysisHaikuMaxTokens < 0 || AnalysisHaikuMaxTokens > 100000)
        throw new InvalidOperationException("Token threshold out of range");
}
```

**Option 2: Use default config if validation fails**
```csharp
try
{
    Load();
    ValidateConfig();
}
catch (Exception ex)
{
    _logger.LogError("Config validation failed, using defaults", ex);
    // Load hardcoded defaults instead
}
```

**Recommendation:** Use **Option 1** (validate on load). Add to `ConfigManager.cs`.

---

## 5. Model Selection Keyboard Navigation Security

### Finding: Simulating Keyboard Input

**File:** `RESEARCH-InputMethods.md` (proposed)  
**Code:**
```csharp
SendKeys.SendWait("^a");
SendKeys.SendWait("^c");
SendKeys.SendWait("{TAB}");
SendKeys.SendWait("{DOWN}");
```

**Risk:**  
1. Keystroke simulation can be intercepted by keyloggers
2. If Claude Desktop is not focused, keys go to wrong window (e.g., email client)
3. No way to verify model actually changed

**Severity:** MEDIUM  
**Likelihood:** MEDIUM (depends on what's in focus)  
**Impact:** User could accidentally select wrong model or send keystroke to wrong app

### Mitigation (For Sonnet)

**Option 1: Verify focus before sending keys**
```csharp
public bool SelectModelViaKeyboard(string targetModel)
{
    // Verify Claude is focused
    var handle = GetClaudeWindowHandle();
    if (!IsWindowFocused(handle))
    {
        _logger.LogEvent("WARN", ("reason", "Claude not focused, aborting keyboard nav"));
        return false;
    }
    
    // Now send keys
    SendKeys.SendWait("{DOWN}");
    // ...
}
```

**Option 2: After selection, verify model actually changed**
```csharp
// After clicking, read the current model from UI and verify
if (!VerifyModelChanged(targetModel))
{
    _logger.LogError("Model selection verification failed");
    return false;
}
```

**Option 3: Show user confirmation before sending keys**
- Popup says "About to switch to Sonnet via keyboard. Proceed?"
- User manually approves
- Reduces risk of accidental selection

**Recommendation:** Use **Option 1 + 3** (verify focus + show confirmation).

---

## 6. Information Disclosure: Pick History in Memory

### Finding: `_pickLog` in Memory

**File:** `ClaudeMonitorService.cs:13`  
**Code:**
```csharp
private List<ModelPickLog> _pickLog = new();
```

**Risk:** Pick history stored in memory. If app crashes or memory dump taken, log is exposed.

**Severity:** VERY LOW  
**Likelihood:** VERY LOW (requires memory dump or crash)  
**Impact:** Local user can see their own model picks (not sensitive)

### Mitigation: Not necessary

This is acceptable risk. Model picks are not sensitive (just "I picked Haiku for this prompt").

---

## 7. Dependency Vulnerabilities

### Finding: Third-Party NuGet Packages

**File:** `ClaudeModelPicker.csproj`

| Package | Version | Notes |
|---------|---------|-------|
| SharpHook | 5.3.0 | Keyboard hook, maintained |
| FlaUI.Core | 4.0.0 | UI Automation, actively maintained |
| FlaUI.UIA3 | 4.0.0 | ^^ |
| tiktoken-net | 1.0.6 | Token counting, third-party (not official) |
| Newtonsoft.Json | 13.0.3 | De facto standard, secure |
| System.Management | 5.0.0 | Microsoft package, secure |

**Risk:** `tiktoken-net` is not official OpenAI library (Anthropic uses different tokenizer).

**Severity:** LOW  
**Likelihood:** LOW (token counting is not security-critical)  
**Impact:** Token counts may be inaccurate, but won't cause crashes

### Mitigation (For Sonnet)

Consider replacing `tiktoken-net` with official Anthropic tokenizer if available, or document that token counts are estimates.

```csharp
// In PromptAnalyzer.cs, add comment:
/// Token counting uses SharpToken (OpenAI cl100k encoding).
/// For accurate Claude token counts, consider using Anthropic's official tokenizer.
/// This is an estimate only.
private int CountTokens(string text)
```

---

## 8. Admin Privilege Escalation Risk

### Finding: App Runs with Keyboard Hook

**Risk:** If user approves UAC elevation, app gains admin privileges. An attacker could:
- Modify other apps' keystrokes
- Read other windows' input
- Escalate to system privileges

**Severity:** MEDIUM  
**Likelihood:** LOW (app runs with admin, but doesn't escalate further)  
**Impact:** Malware could use hook to steal keystrokes from other apps

### Mitigation

**This is by design.** Keyboard hooks require admin. Document it clearly:

**In README.md:**
```markdown
## Security Notes

ClaudeModelPicker requires administrator privileges to intercept keyboard input globally.

- Do NOT run this app if you do not trust the source
- The app does not escalate privileges further
- If compromised, it could theoretically intercept all keyboard input
- Keep the app updated and verify checksum on downloads
```

**Recommendation:** Add checksum verification in download docs (future work for Opus).

---

## Summary Table

| Issue | Severity | Likelihood | Mitigation | Priority |
|-------|----------|------------|-----------|----------|
| Credential leakage in logs | MEDIUM | HIGH | Sanitize prompts before logging | P1 |
| Clipboard interference | LOW | MEDIUM | Don't restore clipboard | P2 |
| Admin privilege requirement | LOW | HIGH | Document requirement, show warning | P2 |
| Config injection | LOW | LOW | Validate config on load | P3 |
| Keyboard nav focus issue | MEDIUM | MEDIUM | Verify focus, show confirmation | P1 |
| Memory history exposure | VERY LOW | VERY LOW | None needed | P4 |
| Dependency vulnerabilities | LOW | LOW | Document estimates | P3 |
| Admin escalation risk | MEDIUM | LOW | Document, verify checksums | P2 |

---

## Recommendations for Sonnet

1. **P1 (Must do):**
   - Add prompt sanitization (Option 2 from Finding #1)
   - Add focus verification before keyboard navigation (Option 1 from Finding #5)

2. **P2 (Should do):**
   - Add admin check on startup (Finding #3)
   - Document security assumptions in README.md
   - Don't restore clipboard (Finding #2)

3. **P3 (Nice to have):**
   - Config validation (Finding #4)
   - Token counting note (Finding #7)

4. **P4 (Future/Opus):**
   - Checksum verification for downloads
   - Rate limiting on hook events (prevent spam)
   - Telemetry audit (if analytics added)

---

## Test Plan for Sonnet

Before shipping, test:

1. **Credential Leakage:**
   - Type "my API key is sk-123456789abcdef" in Claude
   - Check logs don't contain the full key

2. **Clipboard:**
   - Copy "secret text"
   - Run clipboard read method
   - Verify clipboard still has "secret text"

3. **Admin Check:**
   - Run as regular user, verify warning appears
   - Run as admin, verify hook fires

4. **Config Validation:**
   - Manually edit config.json with invalid values
   - Run app, verify it doesn't crash

5. **Keyboard Navigation Focus:**
   - Open email client, click in it
   - Run app, verify it doesn't type in email client
