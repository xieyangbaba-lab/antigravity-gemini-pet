$ErrorActionPreference = "Stop"

$pluginDir = Join-Path $env:USERPROFILE ".gemini\config\plugins\gemini-catgirl-pet"
if (Test-Path $pluginDir) {
    Remove-Item -Recurse -Force $pluginDir
    Write-Host "Removed Antigravity plugin from: $pluginDir" -ForegroundColor Green
    Write-Host "Restart Antigravity to complete uninstall." -ForegroundColor Cyan
} else {
    Write-Host "Plugin folder not found (already uninstalled): $pluginDir" -ForegroundColor Yellow
}
