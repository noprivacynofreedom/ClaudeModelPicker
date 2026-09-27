# Push ClaudeModelPicker to GitHub

The repo is initialized and committed locally. Run these commands on your Windows machine:

## Option A: Create repo, then push (recommended)

```powershell
# Navigate to the project directory
cd C:\path\to\ClaudeModelPicker

# Create the repo on GitHub (interactive)
gh repo create noprivacynofreedom/ClaudeModelPicker --public --source=. --remote=origin --push
```

## Option B: Manual push (if gh repo create doesn't work)

```powershell
# Add remote
git remote add origin https://github.com/noprivacynofreedom/ClaudeModelPicker.git

# Push
git branch -M main
git push -u origin main
```

## After push

Verify it's up:
```
https://github.com/noprivacynofreedom/ClaudeModelPicker
```

Then clone on Windows and test:
```powershell
git clone https://github.com/noprivacynofreedom/ClaudeModelPicker.git
cd ClaudeModelPicker
dotnet build
dotnet run
```

---

All 16 files (C#, XAML, project file, READMEs, handoffs) are committed and ready to push.
