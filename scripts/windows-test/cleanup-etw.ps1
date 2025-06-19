# Cleanup ETW sessions
# ETWセッションのクリーンアップ

Write-Host "ETW Session Cleanup" -ForegroundColor Yellow
Write-Host "==================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Administrator privileges confirmed" -ForegroundColor Green

# List current ETW sessions
Write-Host "`nListing current ETW sessions..." -ForegroundColor Cyan
try {
    $sessions = logman query -ets
    Write-Host "Current ETW sessions:" -ForegroundColor Gray
    $sessions | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
}
catch {
    Write-Host "Error listing ETW sessions: $_" -ForegroundColor Red
}

# Stop ProcTail related sessions
Write-Host "`nStopping ProcTail related ETW sessions..." -ForegroundColor Cyan
$etwSessions = @(
    "ProcTail",
    "ProcTail-Dev", 
    "NT Kernel Logger"
)

foreach ($session in $etwSessions) {
    try {
        $result = logman stop $session -ets 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Stopped ETW session: $session" -ForegroundColor Green
        } else {
            Write-Host "⚠ ETW session '$session' was not running" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "⚠ Error stopping $session`: $_" -ForegroundColor Yellow
    }
}

# Additional cleanup commands
Write-Host "`nPerforming additional ETW cleanup..." -ForegroundColor Cyan

# Stop any remaining kernel sessions
try {
    wevtutil el | Where-Object { $_ -like "*ProcTail*" } | ForEach-Object {
        Write-Host "Found ProcTail event log: $_" -ForegroundColor Gray
    }
}
catch {
    Write-Host "No ProcTail event logs found" -ForegroundColor Gray
}

# Use PowerShell Get-EtwTraceSession if available
try {
    if (Get-Command Get-EtwTraceSession -ErrorAction SilentlyContinue) {
        $traceSessions = Get-EtwTraceSession | Where-Object { $_.SessionName -like "*ProcTail*" -or $_.SessionName -like "*Kernel*" }
        foreach ($session in $traceSessions) {
            Write-Host "Found trace session: $($session.SessionName)" -ForegroundColor Gray
            try {
                Remove-EtwTraceSession -Name $session.SessionName
                Write-Host "✓ Removed trace session: $($session.SessionName)" -ForegroundColor Green
            }
            catch {
                Write-Host "⚠ Could not remove $($session.SessionName): $_" -ForegroundColor Yellow
            }
        }
    }
}
catch {
    Write-Host "Get-EtwTraceSession not available" -ForegroundColor Gray
}

Write-Host "`nETW cleanup completed!" -ForegroundColor Green
Read-Host "Press Enter to exit"