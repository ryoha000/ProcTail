# Direct Host Test - Run Host directly to see error output
# Hostを直接実行してエラー出力を確認

Write-Host "Direct Host Test" -ForegroundColor Yellow
Write-Host "================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Administrator privileges confirmed" -ForegroundColor Green

# Get paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$hostPath = Join-Path $projectRoot "publish\host\ProcTail.Host.exe"

Write-Host "`nHost path: $hostPath" -ForegroundColor Cyan

if (-not (Test-Path $hostPath)) {
    Write-Host "ERROR: Host executable not found!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Host executable found" -ForegroundColor Green

# Create log directory
$logDir = "C:\ProcTail-Test-Logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

# Run Host directly and capture output
Write-Host "`nRunning Host directly to capture error output..." -ForegroundColor Yellow
Write-Host "Press Ctrl+C to stop when you see the error" -ForegroundColor Gray
Write-Host "========================================" -ForegroundColor Gray

try {
    # Change to Host directory first
    $hostDir = Split-Path -Parent $hostPath
    Write-Host "Changing to Host directory: $hostDir" -ForegroundColor Gray
    Push-Location $hostDir
    
    # Run Host directly
    & $hostPath 2>&1 | Tee-Object -FilePath "$logDir\direct-host-output.log"
}
catch {
    Write-Host "`nHost execution failed with error:" -ForegroundColor Red
    Write-Host $_ -ForegroundColor Red
}
finally {
    Pop-Location
}

Write-Host "`n========================================" -ForegroundColor Gray
Write-Host "Host output saved to: $logDir\direct-host-output.log" -ForegroundColor Cyan

# Try to read event log for additional errors
Write-Host "`nChecking Windows Event Log for errors..." -ForegroundColor Yellow
try {
    $events = Get-EventLog -LogName Application -Source ".NET Runtime" -Newest 10 -ErrorAction SilentlyContinue | 
              Where-Object { $_.TimeGenerated -gt (Get-Date).AddMinutes(-5) }
    
    if ($events) {
        Write-Host "Recent .NET Runtime errors found:" -ForegroundColor Yellow
        foreach ($event in $events) {
            Write-Host "Time: $($event.TimeGenerated)" -ForegroundColor Gray
            Write-Host "Message: $($event.Message)" -ForegroundColor Gray
            Write-Host "---" -ForegroundColor Gray
        }
    }
}
catch {
    Write-Host "Could not read event log" -ForegroundColor Gray
}

Read-Host "`nPress Enter to exit"