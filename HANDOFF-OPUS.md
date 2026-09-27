# Claude Model Picker — Handoff to Opus (Final Review & Validation)

**What this is:** A Windows app that hooks into Claude Desktop to auto-pick and switch AI models based on prompt analysis. This is post-Sonnet cleanup, so code should be solid.

**Your role:** Final validation on architecture, safety, security, and production readiness.

---

## **Architecture Review**

### Questions to answer:
1. **Is the keyboard hook design sound?**
   - Global hook in Windows runs in a critical thread. Any blocking call freezes global input.
   - Is the code async-first to prevent this?
   - Are there deadlock risks?

2. **UI Automation fragility:**
   - FlaUI uses UIA (UI Automation), which works for native apps but Electron + React is risky.
   - Has Sonnet added fallbacks (clipboard, keyboard nav)?
   - If Claude Desktop updates, does this break silently or noisily?
   - Is there a manual override (hotkey to pick model)?

3. **Token counting accuracy:**
   - Using `tiktoken` (OpenAI's tokenizer). Is this correct for Claude models?
   - Verify: Haiku/Sonnet use same tokenization?
   - What's the performance impact? Is it cached?

4. **Model picking heuristics:**
   - Current: <150 tokens → Haiku, >300 → Sonnet, keywords weighted
   - Is this reasonable? Should there be more nuance (task type, user tier)?
   - Are thresholds configurable?

---

## **Security & Privacy**

### Critical checks:
- ⚠️ **Admin privileges:** Global keyboard hook needs admin. Document this clearly.
- ⚠️ **Prompt logging:** App logs prompts (first 100 chars). Is this safe?
  - Could expose API keys, secrets, private info?
  - Should logging be opt-in? Encrypted? Rate-limited?
  - Where are logs stored? Accessible to other users?
- ⚠️ **UI Automation attacks:** Could another app abuse the hook to click malicious UI?
  - Are we vulnerable to privilege escalation?
- ⚠️ **Dependencies:** Check tiktoken-net, SharpHook for known CVEs.

---

## **Stability & Reliability**

### Things that can go wrong:
1. **Claude Desktop updates:** UI selectors change → auto-click fails silently. How do we surface this?
2. **Admin prompt:** Windows may ask for elevation. Does the app handle this gracefully?
3. **Hook unregisters:** System could forcibly remove global hook. Does app recover?
4. **Rapid Enter presses:** Does queueing/throttling prevent chaos?
5. **No Claude Desktop running:** App should just be a no-op, not crash.
6. **Slow model pick (tiktoken first-run):** Does user experience a UI freeze?

**Questions:**
- Are there tests for all failure modes?
- Does the app log enough to debug issues remotely?
- Is there a safe mode / fallback mechanism?

---

## **Feature Completeness**

### Expected by user:
- ✅ Auto-picks model on Enter
- ✅ Shows recommendation popup if uncertain
- ✅ Tracks usage history
- ⚠️ **Dashboard (not in v1):** User might expect to see stats, usage trends. Is this in scope?
- ⚠️ **Tray icon:** App needs visual feedback it's running. Does it have one?
- ⚠️ **Settings:** Can users tune thresholds, or is it hardcoded?

**Ask Sonnet:** What's actually implemented vs. what's left for v2?

---

## **Performance**

- First `tiktoken` load: how long? (tens of ms? seconds?)
- FlaUI window finding: cached or fresh each time?
- Any memory leaks in the global hook or UI searches?
- Long-running test: does app stay stable after 1000 picks?

---

## **Code Quality Checklist**

- [ ] All public methods have XML doc comments
- [ ] No hard-coded thresholds (all in config)
- [ ] Async-first design (no blocking in hook)
- [ ] Structured logging (not just Debug.WriteLine)
- [ ] Unit tests for logic (PromptAnalyzer)
- [ ] Integration tests for UI automation (or at least documented edge cases)
- [ ] No secrets in code (API keys, tokens, paths)
- [ ] Proper exception handling (no silent failures)
- [ ] Readme with setup, troubleshooting, logs location

---

## **Sign-Off Questions**

Before ship, Opus should affirm:

1. **Is the architecture sound for this problem?**
   - Or should we pivot to a different approach (e.g., Claude API proxy, browser extension only)?

2. **Are the security implications acceptable?**
   - Admin rights, prompt logging, global hook—are these risks documented and mitigated?

3. **Is the reliability acceptable for a daily-use tool?**
   - What's the failure mode? (Graceful no-op, or crash?)

4. **Is the code ready for handoff to a maintainer?**
   - Can someone else pick this up, understand it, and fix bugs?

---

## **Deliverable**

A short **OPUS-REVIEW.md** with:
- ✅/⚠️/❌ on each major area (architecture, security, stability, code quality)
- Top 3 risks and mitigations
- Recommended next steps (ship v1, fix X before ship, pivot approach, etc)
- Sign-off: "Ready for production" or "Needs work: [list]"

---

**Tone:** You're the architect. If something feels off, push back. This tool runs with admin rights and reads user prompts—it has to be trustworthy.
