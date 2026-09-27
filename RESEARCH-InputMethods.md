# Task 1: Clipboard + Keyboard Navigation Research

## Problem
FlaUI UI Automation fails on Electron (Claude Desktop), so we need fallback methods to:
1. Read the user's prompt from the input field
2. Select a model (Haiku/Sonnet) without clicking UI elements

## Solution 1: Clipboard-Based Prompt Reading

### How It Works
When user types, the app intercepts Enter. Before the keypress fires, we:
1. Select all text in Claude's input field (`Ctrl+A`)
2. Copy to clipboard (`Ctrl+C`)
3. Read clipboard text
4. Restore clipboard (save original, paste back)

### C# Implementation

```csharp
using System.Windows.Forms;

public class ClipboardInputReader
{
    /// <summary>
    /// Read prompt from Claude's input field via clipboard.
    /// Saves/restores clipboard to avoid side effects.
    /// </summary>
    public string ReadPromptViaClipboard()
    {
        string savedClipboard = null;
        try
        {
            // Save current clipboard content
            if (Clipboard.ContainsText())
                savedClipboard = Clipboard.GetText();

            // Select all and copy
            SendKeys.SendWait("^a");        // Ctrl+A
            Thread.Sleep(50);
            SendKeys.SendWait("^c");        // Ctrl+C
            Thread.Sleep(100);

            // Read clipboard
            string prompt = Clipboard.GetText() ?? string.Empty;

            // Restore clipboard (optional; set to false to keep copied text)
            if (savedClipboard != null)
                Clipboard.SetText(savedClipboard);

            return prompt;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Clipboard read failed: {ex.Message}");
            return string.Empty;
        }
    }
}
```

### Pros & Cons
| Pros | Cons |
|------|------|
| Works on Electron | Modifies clipboard (can be restored) |
| Simple, no UI automation | May fail if Ctrl+A/C blocked by Claude |
| Reliable text capture | Requires user's input field to be focused |

### Testing Steps (Sonnet)
1. Open Claude Desktop, focus input field
2. Type: "Can you explain quantum computing?"
3. Call `ReadPromptViaClipboard()` before pressing Enter
4. Verify it returns: "Can you explain quantum computing?"
5. Check clipboard still has your saved content (if you saved it)

---

## Solution 2: Keyboard Navigation for Model Selection

### How It Works
Instead of clicking the model dropdown with FlaUI, we use keyboard navigation:
1. Tab to focus the model selector button
2. Press Tab/Shift+Tab to navigate menu items
3. Arrow keys (Up/Down) to select Haiku or Sonnet
4. Press Enter to confirm

### C# Implementation

```csharp
using System.Windows.Forms;

public class KeyboardModelSelector
{
    /// <summary>
    /// Select model via keyboard navigation.
    /// Assumes Claude Desktop model selector is accessible via Tab.
    /// </summary>
    public bool SelectModelViaKeyboard(string targetModel)
    {
        try
        {
            // Press Tab multiple times to reach model selector
            // (exact count depends on Claude's UI; may need tuning)
            for (int i = 0; i < 5; i++)
            {
                SendKeys.SendWait("{TAB}");
                Thread.Sleep(100);
            }

            // Open dropdown with spacebar or Enter
            SendKeys.SendWait(" ");  // Spacebar to open dropdown
            Thread.Sleep(200);

            // Navigate with arrow keys to target model
            // If current model is Haiku and we want Sonnet, press Down once
            if (targetModel == "Sonnet")
            {
                SendKeys.SendWait("{DOWN}");
            }
            else if (targetModel == "Haiku")
            {
                SendKeys.SendWait("{UP}");
            }

            Thread.Sleep(100);

            // Confirm selection
            SendKeys.SendWait("{ENTER}");
            Thread.Sleep(300);

            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Keyboard selection failed: {ex.Message}");
            return false;
        }
    }
}
```

### Pros & Cons
| Pros | Cons |
|------|------|
| No UI automation, pure keyboard | Requires precise Tab count (fragile on UI changes) |
| Works on Electron | May not work if model selector isn't keyboard-navigable |
| No clipboard side effects | Timing-dependent (may need Thread.Sleep tuning) |

### Testing Steps (Sonnet)
1. Open Claude Desktop
2. Verify current model (e.g., Haiku)
3. Call `SelectModelViaKeyboard("Sonnet")`
4. Check if model switched to Sonnet
5. Repeat with Haiku to test both directions

---

## Hybrid Approach (Recommended for Sonnet)

Combine both methods with fallback:

```csharp
public class InputMethodManager
{
    private readonly ClipboardInputReader _clipboardReader = new();
    private readonly KeyboardModelSelector _keyboardSelector = new();
    private readonly ClaudeMonitorService _fallbackMonitor = new();

    public string ReadPrompt()
    {
        // Try clipboard first (most reliable)
        string prompt = _clipboardReader.ReadPromptViaClipboard();
        
        if (!string.IsNullOrEmpty(prompt))
            return prompt;

        // Fallback to FlaUI (legacy, may fail on Electron)
        return _fallbackMonitor.ReadClaudeInputField();
    }

    public bool SelectModel(string model)
    {
        // Try keyboard navigation first
        if (_keyboardSelector.SelectModelViaKeyboard(model))
            return true;

        // Fallback to FlaUI (legacy)
        return _fallbackMonitor.TryClickModelDropdown(model);
    }
}
```

---

## Claude Desktop Model Selector Research

**Current Status:** Unknown if Tab navigation works on Claude's model selector.

**To Verify (Sonnet should test):**
1. Open Claude Desktop
2. Press Tab repeatedly and note UI focus order
3. Check if Tab reaches the model selector button
4. Try pressing Space or Enter to open dropdown
5. Try Arrow keys to navigate Haiku/Sonnet options
6. Document findings; adjust Tab count and key sequences as needed

**Known Claude Desktop UI Elements:**
- Main window title: "Claude"
- Model selector likely a ComboBox or Button near top-right
- Input field is a textarea in the message composition area

---

## Integration Notes for Sonnet

1. Add `using System.Windows.Forms;` to project (already included via WPF reference)
2. Update `ClaudeMonitorService.cs`:
   - Inject or create `InputMethodManager`
   - Replace FlaUI calls with hybrid approach
   - Keep FlaUI code as fallback (don't delete)
3. Test with real Claude Desktop, not mocks
4. Log which method succeeded (clipboard vs FlaUI) for debugging
5. Add timing tweaks based on real-world testing (Thread.Sleep values)

---

## Questions for Sonnet

- Does Claude Desktop model selector support Tab navigation?
- What is the correct Tab count to reach the model selector?
- Does Arrow-Up/Down work to navigate between Haiku and Sonnet?
- Should we restore clipboard content or leave it as-is?
