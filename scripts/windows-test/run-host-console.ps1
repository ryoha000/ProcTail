# Run Host in console to see real-time output
# Hostをコンソールで実行してリアルタイム出力を確認

Write-Host "Running Host in Console Mode" -ForegroundColor Yellow
Write-Host "============================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Kill existing Host processes
Write-Host "`nKilling existing Host processes..." -ForegroundColor Cyan
Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# Local path
$localHostPath = "C:\Temp\ProcTailTest\host\ProcTail.Host.exe"
$localHostDir = "C:\Temp\ProcTailTest\host"

if (-not (Test-Path $localHostPath)) {
    Write-Host "ERROR: Host not found at: $localHostPath" -ForegroundColor Red
    Write-Host "Please run run-test-local.ps1 first to copy files" -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

# Set environment for detailed logging
$env:DOTNET_ENVIRONMENT = "Development"
$env:Logging__LogLevel__Default = "Debug"
$env:Logging__LogLevel__Microsoft = "Information"

Write-Host "`nStarting Host in console mode..." -ForegroundColor Yellow
Write-Host "Working directory: $localHostDir" -ForegroundColor Gray
Write-Host "Press Ctrl+C to stop" -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Gray

try {
    Push-Location $localHostDir
    
    # Run Host directly in console
    & $localHostPath
    
    Write-Host "`nHost exited with code: $LASTEXITCODE" -ForegroundColor Cyan
}
catch {
    Write-Host "`nHost execution error: $_" -ForegroundColor Red
}
finally {
    Pop-Location
}

Read-Host "`nPress Enter to exit"