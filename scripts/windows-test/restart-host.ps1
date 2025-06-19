# Restart Host with updated configuration
# 更新された設定でHostを再起動

Write-Host "Restarting ProcTail Host" -ForegroundColor Yellow
Write-Host "========================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Stop existing Host
Write-Host "`nStopping existing Host processes..." -ForegroundColor Cyan
$existingHosts = Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue
if ($existingHosts) {
    foreach ($proc in $existingHosts) {
        Write-Host "Stopping Host PID: $($proc.Id)" -ForegroundColor Gray
        $proc | Stop-Process -Force
    }
    Start-Sleep -Seconds 3
    Write-Host "✓ Existing processes stopped" -ForegroundColor Green
} else {
    Write-Host "No existing Host processes found" -ForegroundColor Gray
}

# ETW cleanup
Write-Host "`nCleaning up ETW sessions..." -ForegroundColor Cyan
$etwSessions = @("ProcTail", "ProcTail-Dev")
foreach ($session in $etwSessions) {
    try {
        logman stop $session -ets 2>$null
        Write-Host "Stopped ETW session: $session" -ForegroundColor Gray
    }
    catch {
        Write-Host "ETW session '$session' was not running" -ForegroundColor Gray
    }
}

# Start Host
$localHostPath = "C:\Temp\ProcTailTest\host\ProcTail.Host.exe"
$localHostDir = "C:\Temp\ProcTailTest\host"

if (-not (Test-Path $localHostPath)) {
    Write-Host "Host executable not found: $localHostPath" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "`nStarting Host with updated configuration..." -ForegroundColor Cyan
Write-Host "Path: $localHostPath" -ForegroundColor Gray

try {
    $hostProcess = Start-Process -FilePath $localHostPath -PassThru -WorkingDirectory $localHostDir
    Write-Host "Host started with PID: $($hostProcess.Id)" -ForegroundColor Green
    
    # Wait for initialization
    Write-Host "Waiting for Host initialization..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    
    # Check if still running
    if (Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue) {
        Write-Host "✓ Host is running successfully" -ForegroundColor Green
        
        # Test connection
        $localCliPath = "C:\Temp\ProcTailTest\cli\proctail.exe"
        if (Test-Path $localCliPath) {
            Write-Host "`nTesting connection..." -ForegroundColor Cyan
            $testResult = & $localCliPath status 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✓ Connection successful!" -ForegroundColor Green
                Write-Host "Status: $testResult" -ForegroundColor Gray
            } else {
                Write-Host "⚠ Connection failed: $testResult" -ForegroundColor Yellow
            }
        }
        
        Write-Host "`n✅ Host restart completed successfully!" -ForegroundColor Green
        Write-Host "You can now run tests with the updated filtering configuration." -ForegroundColor Cyan
    } else {
        Write-Host "✗ Host exited unexpectedly" -ForegroundColor Red
    }
}
catch {
    Write-Host "✗ Failed to start Host: $_" -ForegroundColor Red
}

Read-Host "`nPress Enter to exit"