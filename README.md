# Claude Model Picker

Auto-picks and switches Claude models (Haiku vs Sonnet) based on your prompt, right in Claude Desktop.

## What it does

1. **Global keyboard hook** monitors when you press Enter in Claude Desktop
2. **Reads your prompt** from the input field
3. **Analyzes it** (token count + keywords) to decide: Haiku or Sonnet?
4. **Auto-clicks** the model selector to switch, or shows a popup to confirm
5. **Logs picks** for tuning over time

## Project Structure

```
ClaudeModelPicker/
├── App.xaml / App.xaml.cs       # WPF app entry, hook startup
├── MainWindow.xaml / .cs        # Tray window (hidden)
├── ModelPickDialog.xaml / .cs   # Popup recommendation dialog
├── Services/
│   ├── KeyboardHookService.cs   # Global Enter key intercept
│   ├── ClaudeMonitorService.cs  # Input reader, modal selector, logger
│   └── PromptAnalyzer.cs        # Token counting + heuristics
├── Models/
│   ├── ModelPick.cs             # Recommendation result
│   └── ModelPickLog.cs          # Pick history log
├── ClaudeModelPicker.csproj     # Project file (net8.0-windows)
└── HANDOFF-*.md                 # Handoff docs (see below)
```

## Build & Run

**Requirements:**
- Windows 10+
- .NET 8 SDK
- Admin privileges (global keyboard hook)

**Build:**
```bash
cd ClaudeModelPicker
dotnet build -c Release
```

**Run:**
```bash
./bin/Release/net8.0-windows/ClaudeModelPicker.exe
```

The app minimizes to tray and runs in the background.

## How It Works

### Keyboard Hook
When you press **Enter** in Claude Desktop:
1. Hook fires globally (SharpHook)
2. Reads Claude's input field (FlaUI + UI Automation)
3. Analyzes prompt: token count + keyword scoring
4. Decision: pick model or show popup

### Model Picking Logic
- **Haiku** (fast): <150 tokens, simple tasks (summarize, list, extract, facts)
- **Sonnet** (powerful): >300 tokens, complex tasks (analyze, design, debug, reason)
- **Uncertain** (50-80%): shows popup for manual choice

### Fallback Behavior
- **Auto-click fails** → popup dialog
- **Both fail** → do nothing (let you choose manually)
- **Claude not running** → silent no-op

## Handoff Documents

Three review stages:

### 1. HANDOFF-HAIKU.md (~$10, cloud work)
What Haiku (me) needs to do:
- Compile test
- Verify keyboard hook fires
- Test FlaUI input reading
- Test model dropdown clicking
- Document findings + blockers

**Deliverable:** FINDINGS.md + (if works) working .exe

### 2. HANDOFF-SONNET.md (code cleanup)
What Sonnet should fix:
- UI Automation robustness (add fallbacks)
- Keyboard hook async handling (no blocking)
- Error handling (structured logging)
- Code quality (organization, comments, config)
- Missing features (tray icon, settings, dashboard)
- Tests

**Deliverable:** Production-ready code

### 3. HANDOFF-OPUS.md (final review)
What Opus should validate:
- Architecture soundness
- Security/privacy risks
- Stability & reliability
- Code quality sign-off
- Risk assessment + ship decision

**Deliverable:** OPUS-REVIEW.md (✅/⚠️/❌ sign-off)

## Known Limitations

1. **Fragile UI Automation:**
   - Claude Desktop is Electron. UI selectors may break on updates.
   - Fallback: clipboard read or keyboard nav (TBD by Sonnet)

2. **Token Counting:**
   - Uses `tiktoken` (OpenAI's tokenizer). Correct for Claude, but verify accuracy.
   - First run may be slow (tokenizer loads).

3. **Heuristics Are Basic:**
   - Keyword list is hardcoded (no learning, no user customization yet).
   - Thresholds (150, 300 tokens) are guesses—need real-world tuning.

4. **No Usage Dashboard Yet:**
   - App logs picks but doesn't visualize `plan-usage-history.json`.
   - That's a v2 feature.

5. **Admin Required:**
   - Global keyboard hook needs elevation. Windows will prompt.

## Next Steps

1. **Haiku:** Build, test, report findings
2. **Sonnet:** Clean up, add fallbacks, improve robustness
3. **Opus:** Review, sign-off, identify risks
4. **Ship v1:** Basic model picker working
5. **v2:** Tray icon, settings, usage dashboard, tuning

## Troubleshooting

**App doesn't intercept Enter:**
- Is Claude Desktop in focus?
- Is the hook running? (Check for exception on startup)
- Is Claude input field detectable by FlaUI? (May need UI selector tuning)

**Model click fails:**
- FlaUI can't find the model dropdown button
- Fallback: manual popup works?
- Next: add keyboard-based selector (Tab + arrow keys)

**App crashes:**
- Global hook is in a critical thread—any exception will propagate
- Check logs (TBD by Sonnet) or run with `--debug` flag

**High latency on Enter:**
- First `tiktoken` load is slow (~500ms)
- Subsequent picks should be fast (<50ms)
- Profile and cache if needed

## Dependencies

- **SharpHook** - Global keyboard hook
- **FlaUI** + **FlaUI.UIA3** - UI Automation (read input, click buttons)
- **tiktoken-net** - Token counting for prompts
- **Newtonsoft.Json** - Config/logging JSON
- **System.Management** - Windows process/event management (future)

## License

Proprietary. Built for Jason's Claude workflow.

---

**Status:** v0.1 (pre-alpha, awaiting Haiku test results)
