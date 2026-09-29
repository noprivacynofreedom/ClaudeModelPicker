# Started by "cmp online" (see atlas\atlas-components\scripts\launcher\apps.json).
# Finds the newest ClaudeModelPicker.exe under bin\ and starts it in the tray.
# It stops by itself after 10 minutes with no Enter in Claude (app.idleShutdownMinutes in config.json).
$proj = 'F:\APP-DEV\projects\ClaudeModelPicker'

if (Get-Process ClaudeModelPicker -ErrorAction SilentlyContinue) {
    Write-Host 'CMP is already running. Look for its icon in the tray.'
    return
}

$exe = Get-ChildItem "$proj\bin" -Recurse -Filter ClaudeModelPicker.exe -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $exe) {
    Write-Host 'CMP is not built yet. Run this first:'
    Write-Host "  dotnet build $proj\ClaudeModelPicker.csproj"
    return
}

Start-Process -FilePath $exe.FullName
Write-Host "CMP online: $($exe.FullName)"
Write-Host 'Stop it with: cmp offline'
