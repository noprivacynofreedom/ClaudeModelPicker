# Claude Model Picker — Handoff to Haiku (Cloud Work) ~$10

**What this is:** First-pass code foundation for a Windows tray app that intercepts Enter in Claude Desktop and auto-picks Haiku vs Sonnet based on prompt analysis.

**What's done:**
- Full C# WPF project structure (net8.0-windows)
- Global keyboard hook service (SharpHook)
- UI Automation reader (FlaUI) for Claude input field
- Prompt analyzer with tiktoken token counting + keyword heuristics
- Model pick dialog (XAML popup)
- Fallback: auto-click model dropdown vs popup on fail
- Logging of pick history

**What needs cloud work (~$10 budget):**

1. **Test compile & dependencies**
   - Run `dotnet build` on the project
   - Verify all NuGet packages resolve (SharpHook, FlaUI, tiktoken-net, etc)
   - Flag version conflicts or missing frameworks

2. **Keyboard hook stability test**
   - Build app
   - Run on a Windows test machine (or describe blockers)
   - Hit Enter in Claude Desktop, verify hook fires
   - Check if input field reading works (may need DOM selectors adjusted)
   - Report: does it intercept? does it read the prompt?

3. **Model selector reliability check**
   - Test: does FlaUI find the Claude model dropdown?
   - Claude Desktop UI might use different selectors than assumed
   - Try to click "Haiku" or "Sonnet" button and report success/failure
   - If it fails, propose fallback (e.g., clipboard paste + keyboard nav)

4. **Edge cases & crashes**
   - Test: what happens if Claude Desktop isn't running?
   - Test: what if input field is empty?
   - Test: rapid Enter presses—does hook choke?
   - Report stability; suggest mutex/throttle if needed

5. **Document findings** in a FINDINGS.md file:
   - Which parts work, which don't
   - Why: UI automation selectors, token counting accuracy, etc
   - Suggestions for next phase (Sonnet cleanup)

**What's intentionally left for Sonnet:**
- Code quality (naming, structure, error handling)
- Missing features (tray icon, settings UI, usage dashboard integration)
- Configuration (heuristic thresholds tunable)
- Tests

**Deliverable:** A FINDINGS.md file + (if buildable) a working .exe

---

**Budget note:** This is ~4-6 hours of integration & testing. If it hits blockers (Claude UI changes, admin privs required, etc), pivot to fallback approaches and report.
