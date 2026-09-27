# Task 1 — Clipboard Read + Keyboard Nav (research for Sonnet)

## Why this replaces FlaUI

Claude Desktop is Electron. Electron renders its UI in Chromium, not native
Win32/UWP controls. FlaUI (UI Automation) only sees what the accessibility
tree exposes. Electron apps expose a much thinner tree than native apps —
often just a generic "Document" or "Pane" node with no named Edit/Button
controls underneath, unless the app's developers went out of their way to
add ARIA labels. `ClaudeMonitorService.ReadClaudeInputField()` and
`TryClickModelDropdown()` both search for named `Edit`/`Button`/`MenuItem`
controls — this is very likely to return nothing on real Claude Desktop.
This can't be verified without a Windows machine running Claude Desktop
(flagged in FINDINGS.md as untested); the code below is written defensively
so it degrades to the popup instead of silently doing nothing.

## Approach A — Read the prompt via clipboard

Steps: focus the input (already focused, since the user just pressed
Enter there), select-all, copy, read clipboard, restore the clipboard
so we don't clobber what the user had copied before.

```csharp
// Services/ClipboardPromptReader.cs
using System.Windows;
using System.Windows.Forms; // System.Windows.Forms.Clipboard is more reliable than WPF's for plain text round-trips

namespace ClaudeModelPicker.Services
{
    public static class ClipboardPromptReader
    {
        // Must be called from the keyboard hook's dispatch, not the raw hook thread —
        // SendKeys / Clipboard require an STA thread with a message pump.
        public static string ReadViaClipboard(InputSimulatorLike input)
        {
            string? previousClipboard = SafeGetClipboardText();

            try
            {
                input.SendCtrlA();
                Thread.Sleep(30); // let the app process selection before copy
                input.SendCtrlC();
                Thread.Sleep(50); // clipboard write is async in Electron/Chromium

                var text = SafeGetClipboardText() ?? string.Empty;
                return text;
            }
            finally
            {
                // Restore whatever was on the clipboard before we touched it.
                if (previousClipboard != null)
                    SafeSetClipboardText(previousClipboard);
                else
                    SafeClearClipboard();
            }
        }

        private static string? SafeGetClipboardText()
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : null;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                // Another process is holding the clipboard lock — common on Windows.
                // Retry once after a short delay.
                Thread.Sleep(50);
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { return null; }
            }
        }

        private static void SafeSetClipboardText(string text)
        {
            try { Clipboard.SetText(text); } catch { /* best effort */ }
        }

        private static void SafeClearClipboard()
        {
            try { Clipboard.Clear(); } catch { /* best effort */ }
        }
    }
}
```

`InputSimulatorLike` is a thin wrapper interface Sonnet should define
around whatever key-send library is already in the project (SharpHook
covers hooking, not synthetic input — a separate small sender is needed,
e.g. `SendKeys.SendWait("^a")` / `SendKeys.SendWait("^c")` from
`System.Windows.Forms`, or `InputSimulator` NuGet if more control is
wanted).

**Risk to flag to Jason:** this steals focus/selection state and briefly
overwrites the system clipboard. If the app crashes between copy and
restore, the user's clipboard is left with their own prompt in it — mostly
harmless, but worth a one-line note in the README.

## Approach B — Keyboard nav for the model dropdown

Same logic problem as the input field: FlaUI's `Click()` on a `Button` may
never find the button. Fallback is to drive it blind, by position in the
tab order, not by name:

```csharp
// Pseudocode outline — exact key sequence needs a live Claude Desktop session to confirm
public static bool TryKeyboardNavModelSelect(string targetModel, InputSimulatorLike input)
{
    // 1. Tab from the input field toward the model selector.
    //    Exact tab-stop count is UNKNOWN without testing against the real app —
    //    ship this as a configurable value (see ConfigManager, "modelSelectorTabStops").
    for (int i = 0; i < Config.Current.ModelSelectorTabStops; i++)
        input.SendTab();

    // 2. Open the dropdown (Enter or Space, app-dependent — try Enter first).
    input.SendEnter();
    Thread.Sleep(150);

    // 3. Arrow down to the target model. Order in the dropdown is also unknown —
    //    safest bet: read the list count from config and search by known label
    //    order (Haiku, Sonnet, Opus) rather than hard-coding arrow-key counts.
    // ... arrow key loop ...

    // 4. Confirm.
    input.SendEnter();

    // 5. No reliable way to verify success without UI Automation feedback —
    //    return true optimistically, but always let the popup be the fallback
    //    path if TryKeyboardNavModelSelect() throws or config flags "untested".
    return true;
}
```

**This is the part that most needs Sonnet's hands-on testing against real
Claude Desktop** (flagged in HANDOFF-SONNET.md Priority 2 already — this
doc just gives a starting skeleton, it does not confirm tab-stop counts or
dropdown order, since that requires a Windows box with Claude Desktop
running).

## Recommendation

Given both A and B are unverified against the real app, the safe default
is: **always show the popup**, and treat auto-click (either FlaUI or
keyboard nav) as a best-effort bonus behind a config flag
(`"autoClickEnabled": false` by default — see Task 3 config). This matches
the HANDOFF-SONNET.md deliverable checklist, which explicitly allows
"auto-click can stay broken for now."
