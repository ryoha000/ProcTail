# Complete ETW cleanup including ProcTail_ sessions
# ProcTail_セッションを含む完全なETWクリーンアップ

Write-Host "Complete ETW Session Cleanup" -ForegroundColor Yellow
Write-Host "============================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Administrator privileges confirmed" -ForegroundColor Green

# Kill any existing ProcTail processes first
Write-Host "`nKilling existing ProcTail processes..." -ForegroundColor Cyan
Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Killing ProcTail.Host process (PID: $($_.Id))" -ForegroundColor Yellow
    $_ | Stop-Process -Force -ErrorAction SilentlyContinue
}

Start-Sleep -Seconds 2

# Get current ETW sessions
Write-Host "`nGetting current ETW sessions..." -ForegroundColor Cyan
$sessions = logman query -ets 2>&1

# Find ProcTail related sessions
$procTailSessions = @()
$lines = $sessions -split "`n"
foreach ($line in $lines) {
    if ($line -match "^\s*ProcTail") {
        $sessionName = ($line -split '\s+')[0].Trim()
        if ($sessionName -and $sessionName -ne "ProcTail") {
            $procTailSessions += $sessionName
        }
    }
}

Write-Host "Found ProcTail ETW sessions:" -ForegroundColor Yellow
if ($procTailSessions.Count -eq 0) {
    Write-Host "  (none found)" -ForegroundColor Gray
} else {
    foreach ($session in $procTailSessions) {
        Write-Host "  $session" -ForegroundColor Gray
    }
}

# Stop all ProcTail sessions
Write-Host "`nStopping ProcTail ETW sessions..." -ForegroundColor Cyan
$allSessions = @("ProcTail", "ProcTail-Dev", "NT Kernel Logger") + $procTailSessions

foreach ($session in $allSessions) {
    try {
        Write-Host "Stopping: $session" -ForegroundColor Gray
        $result = logman stop $session -ets 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ Stopped: $session" -ForegroundColor Green
        } else {
            Write-Host "⚠ Session not running or already stopped: $session" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "⚠ Error stopping $session`: $_" -ForegroundColor Yellow
    }
}

# Additional cleanup - try to stop any session with "ProcTail" in the name
Write-Host "`nPerforming comprehensive cleanup..." -ForegroundColor Cyan
$allSessionsOutput = logman query -ets 2>&1
$allLines = $allSessionsOutput -split "`n"

foreach ($line in $allLines) {
    if ($line -match "ProcTail" -and $line -match "トレース\s+実行中") {
        $sessionName = ($line -split '\s+')[0].Trim()
        if ($sessionName -and $sessionName -notmatch "^-+$") {
            try {
                Write-Host "Force stopping: $sessionName" -ForegroundColor Gray
                logman stop $sessionName -ets 2>$null
                Write-Host "✓ Force stopped: $sessionName" -ForegroundColor Green
            }
            catch {
                Write-Host "⚠ Could not force stop: $sessionName" -ForegroundColor Yellow
            }
        }
    }
}

# Wait a moment
Start-Sleep -Seconds 3

# Verify cleanup
Write-Host "`nVerifying cleanup..." -ForegroundColor Cyan
$finalSessions = logman query -ets 2>&1
$finalLines = $finalSessions -split "`n"
$remainingProcTail = @()

foreach ($line in $finalLines) {
    if ($line -match "ProcTail" -and $line -match "実行中") {
        $sessionName = ($line -split '\s+')[0].Trim()
        if ($sessionName) {
            $remainingProcTail += $sessionName
        }
    }
}

if ($remainingProcTail.Count -eq 0) {
    Write-Host "✓ All ProcTail ETW sessions have been stopped!" -ForegroundColor Green
} else {
    Write-Host "⚠ Some ProcTail sessions are still running:" -ForegroundColor Yellow
    foreach ($session in $remainingProcTail) {
        Write-Host "  $session" -ForegroundColor Red
    }
}

Write-Host "`nETW cleanup completed!" -ForegroundColor Green
Write-Host "You can now run the ProcTail Host." -ForegroundColor Cyan

Read-Host "Press Enter to exit"