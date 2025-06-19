# Simple manual test execution
# シンプルなマニュアルテスト実行

Write-Host "ProcTail Simple Test" -ForegroundColor Yellow
Write-Host "===================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Administrator privileges confirmed" -ForegroundColor Green

# Test 1: Check if Host executable exists
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$hostPath = Join-Path $projectRoot "publish\host\ProcTail.Host.exe"
Write-Host "Looking for Host at: $hostPath" -ForegroundColor Gray
$resolvedHostPath = if (Test-Path $hostPath) { $hostPath } else { $null }

if ($resolvedHostPath) {
    Write-Host "✓ Host executable found: $resolvedHostPath" -ForegroundColor Green
} else {
    Write-Host "✗ Host executable NOT found at: $hostPath" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Test 2: Try to start Host
Write-Host "`nStarting Host..." -ForegroundColor Cyan
try {
    Write-Host "Executing: $resolvedHostPath" -ForegroundColor Gray
    $hostProcess = Start-Process -FilePath $resolvedHostPath -PassThru -WindowStyle Normal
    Write-Host "✓ Host started with PID: $($hostProcess.Id)" -ForegroundColor Green
    
    # Wait and check if it's still running
    Start-Sleep -Seconds 3
    $stillRunning = Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue
    
    if ($stillRunning) {
        Write-Host "✓ Host is still running after 3 seconds" -ForegroundColor Green
    } else {
        Write-Host "✗ Host process exited within 3 seconds" -ForegroundColor Red
        Write-Host "Check the logs at C:\ProcTail-Test-Logs\ for error details" -ForegroundColor Yellow
    }
    
    # Stop the host
    if ($stillRunning) {
        Write-Host "Stopping Host..." -ForegroundColor Cyan
        Stop-Process -Id $hostProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host "✓ Host stopped" -ForegroundColor Green
    }
}
catch {
    Write-Host "✗ Failed to start Host: $_" -ForegroundColor Red
}

# Test 3: Check CLI
Write-Host "`nChecking CLI..." -ForegroundColor Cyan
$cliPath = Join-Path $projectRoot "publish\cli\proctail.exe"
Write-Host "Looking for CLI at: $cliPath" -ForegroundColor Gray
$resolvedCliPath = if (Test-Path $cliPath) { $cliPath } else { $null }

if ($resolvedCliPath) {
    Write-Host "✓ CLI executable found: $resolvedCliPath" -ForegroundColor Green
    
    # Try CLI help command
    try {
        $helpOutput = & $resolvedCliPath --help 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ CLI help command works" -ForegroundColor Green
        } else {
            Write-Host "✗ CLI help command failed" -ForegroundColor Red
            Write-Host "Output: $helpOutput" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "✗ Failed to execute CLI: $_" -ForegroundColor Red
    }
} else {
    Write-Host "✗ CLI executable NOT found at: $cliPath" -ForegroundColor Red
}

Write-Host "`nTest completed. Check logs at C:\ProcTail-Test-Logs\ for details." -ForegroundColor Yellow
Read-Host "Press Enter to exit"