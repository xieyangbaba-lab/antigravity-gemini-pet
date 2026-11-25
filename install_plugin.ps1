$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$binDir = Join-Path $root "bin"
$petExe = Join-Path $binDir "GeminiPet.exe"
$notifyExe = Join-Path $binDir "PetNotify.exe"
$hostExe = Join-Path $binDir "PetCompanionHost.exe"

if (-not ((Test-Path $petExe) -and (Test-Path $notifyExe) -and (Test-Path $hostExe))) {
    Write-Host "Binaries missing in bin/, running build.ps1 first..." -ForegroundColor Cyan
    & (Join-Path $root "build.ps1")
}

$pluginDir = Join-Path $env:USERPROFILE ".gemini\config\plugins\gemini-catgirl-pet"
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$notifyJsonPath = $notifyExe.Replace('\', '\\')
$hostJsonPath = $hostExe.Replace('\', '\\')

$pluginJson = @"
{
  "name": "Gemini 猫娘伴生桌宠 (Live2D)",
  "description": "轻量化原生 Live2D 级分层物理桌宠，通过极速本地 Hooks 实时同步 Antigravity 思考、施法干活与完成状态。"
}
"@

$hooksJson = @"
{
  "gemini-catgirl-pet-sync": {
    "SessionStart": [
      {
        "type": "command",
        "command": "\"$notifyJsonPath\" session_start",
        "timeout": 5
      }
    ],
    "PreInvocation": [
      {
        "type": "command",
        "command": "\"$notifyJsonPath\" pre_invocation",
        "timeout": 5
      }
    ],
    "PostToolUse": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "\"$notifyJsonPath\" post_tool",
            "timeout": 5
          }
        ]
      }
    ],
    "Stop": [
      {
        "type": "command",
        "command": "\"$notifyJsonPath\" stop",
        "timeout": 5
      }
    ]
  }
}
"@

$mcpJson = @"
{
  "mcpServers": {
    "gemini-pet-companion": {
      "command": "$hostJsonPath",
      "args": []
    }
  }
}
"@

[System.IO.File]::WriteAllText((Join-Path $pluginDir "plugin.json"), $pluginJson, $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $pluginDir "hooks.json"), $hooksJson, $utf8NoBom)
[System.IO.File]::WriteAllText((Join-Path $pluginDir "mcp_config.json"), $mcpJson, $utf8NoBom)

Write-Host "Antigravity companion plugin installed to:" -ForegroundColor Green
Write-Host "  $pluginDir" -ForegroundColor White
Write-Host "Restart Antigravity (or enable the plugin in Plugins Manager) and Gemini Catgirl Pet will auto-launch and sync!" -ForegroundColor Cyan
