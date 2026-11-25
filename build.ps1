$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$srcDir = Join-Path $root "src"
$binDir = Join-Path $root "bin"
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wpfDir = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF"
if (-not (Test-Path $csc)) {
    throw "Cannot find .NET Framework 4.0 C# compiler at: $csc"
}

# Stop running GeminiPet before replacing binary
Get-Process -Name "GeminiPet" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 250

$petExe = Join-Path $binDir "GeminiPet.exe"
$notifyExe = Join-Path $binDir "PetNotify.exe"
$hostExe = Join-Path $binDir "PetCompanionHost.exe"

Write-Host "[1/3] Compiling GeminiPet.exe..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /optimize+ /out:$petExe `
    /r:"$wpfDir\PresentationFramework.dll" `
    /r:"$wpfDir\PresentationCore.dll" `
    /r:"$wpfDir\WindowsBase.dll" `
    /r:"System.Xaml.dll" `
    /r:"System.Windows.Forms.dll" `
    /r:"System.Drawing.dll" `
    (Join-Path $srcDir "GeminiLive2DPet.cs")
if ($LASTEXITCODE -ne 0) { throw "Failed to compile GeminiPet.exe" }

Write-Host "[2/3] Compiling PetNotify.exe..." -ForegroundColor Cyan
& $csc /nologo /target:exe /optimize+ /out:$notifyExe (Join-Path $srcDir "PetNotify.cs")
if ($LASTEXITCODE -ne 0) { throw "Failed to compile PetNotify.exe" }

Write-Host "[3/3] Compiling PetCompanionHost.exe..." -ForegroundColor Cyan
$hostRunning = Get-Process -Name "PetCompanionHost" -ErrorAction SilentlyContinue
if ($hostRunning) {
    Write-Host "  (Skipped: PetCompanionHost.exe is currently running inside Antigravity. Close Antigravity and re-run build.ps1 if you modified PetCompanionHost.cs.)" -ForegroundColor Yellow
} else {
    & $csc /nologo /target:exe /optimize+ /out:$hostExe (Join-Path $srcDir "PetCompanionHost.cs")
    if ($LASTEXITCODE -ne 0) { throw "Failed to compile PetCompanionHost.exe" }
}

Write-Host "`nBuild completed successfully!" -ForegroundColor Green
Get-ChildItem $binDir -Filter "*.exe" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
