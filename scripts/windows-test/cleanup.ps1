# Cleanup script - Stop existing Host and ETW sessions
# 既存のHost停止とETWセッション停止

Write-Host "Cleaning up existing ProcTail processes and ETW sessions..." -ForegroundColor Yellow

# Stop ProcTail Host process
Write-Host "Stopping ProcTail Host process..." -ForegroundColor Cyan
Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Stop any ETW sessions that might be running
Write-Host "Stopping ETW sessions..." -ForegroundColor Cyan
$etwSessions = @("ProcTail", "NT Kernel Logger")
foreach ($session in $etwSessions) {
    try {
        logman stop $session -ets 2>$null
        Write-Host "Stopped ETW session: $session" -ForegroundColor Green
    }
    catch {
        Write-Host "ETW session $session was not running or failed to stop" -ForegroundColor Yellow
    }
}

# Wait a moment for cleanup
Start-Sleep -Seconds 2

Write-Host "Cleanup completed!" -ForegroundColor Green