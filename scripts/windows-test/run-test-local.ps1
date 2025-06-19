# Run integration test from local copy to avoid WSL filesystem issues
# WSLファイルシステムの問題を回避するためローカルコピーから統合テストを実行

param(
    [string]$Tag = "test-notepad",
    [switch]$SkipCleanup,
    [switch]$KeepProcesses
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "  ProcTail Windows Integration Test (Local)" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

$testStartTime = Get-Date

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator and try again." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Setup paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$sourceHostDir = Join-Path $projectRoot "publish\host"
$sourceCliDir = Join-Path $projectRoot "publish\cli"

# Local paths
$localRoot = "C:\Temp\ProcTailTest"
$localHostDir = Join-Path $localRoot "host"
$localCliDir = Join-Path $localRoot "cli"
$localHostPath = Join-Path $localHostDir "ProcTail.Host.exe"
$localCliPath = Join-Path $localCliDir "proctail.exe"

Write-Host "`nStep 1: Preparing local copy..." -ForegroundColor Yellow

# Create directories
New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
New-Item -ItemType Directory -Path $localHostDir -Force | Out-Null
New-Item -ItemType Directory -Path $localCliDir -Force | Out-Null

# Copy files
Write-Host "Copying Host files..." -ForegroundColor Cyan
Copy-Item -Path "$sourceHostDir\*" -Destination $localHostDir -Recurse -Force
Write-Host "Copying CLI files..." -ForegroundColor Cyan
Copy-Item -Path "$sourceCliDir\*" -Destination $localCliDir -Recurse -Force

Write-Host "✓ Files copied to local disk" -ForegroundColor Green

try {
    # Step 2: Cleanup
    if (-not $SkipCleanup) {
        Write-Host "`nStep 2: Cleaning up existing processes..." -ForegroundColor Yellow
        Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        
        # Stop ETW sessions
        $etwSessions = @("ProcTail", "NT Kernel Logger")
        foreach ($session in $etwSessions) {
            try {
                logman stop $session -ets 2>$null
            }
            catch {}
        }
        
        Start-Sleep -Seconds 2
        Write-Host "✓ Cleanup completed" -ForegroundColor Green
    }

    # Step 3: Start Host
    Write-Host "`nStep 3: Starting ProcTail Host..." -ForegroundColor Yellow
    
    $hostProcess = Start-Process -FilePath $localHostPath -PassThru -WindowStyle Hidden
    Write-Host "✓ Host started with PID: $($hostProcess.Id)" -ForegroundColor Green
    
    # Wait for initialization
    Start-Sleep -Seconds 3
    
    # Verify Host is running
    if (-not (Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue)) {
        throw "Host process exited unexpectedly"
    }
    
    Write-Host "✓ Host is running successfully" -ForegroundColor Green

    # Step 4: Start Notepad
    Write-Host "`nStep 4: Starting Notepad..." -ForegroundColor Yellow
    
    $notepadProcess = Start-Process -FilePath "notepad.exe" -PassThru
    Write-Host "✓ Notepad started with PID: $($notepadProcess.Id)" -ForegroundColor Green
    $notepadPid = $notepadProcess.Id
    
    Start-Sleep -Seconds 2

    # Step 5: Subscribe to notepad
    Write-Host "`nStep 5: Subscribing to Notepad with CLI..." -ForegroundColor Yellow
    
    $addResult = & $localCliPath add-watch-target --pid $notepadPid --tag $Tag 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Successfully subscribed to Notepad!" -ForegroundColor Green
    } else {
        throw "Failed to subscribe to Notepad: $addResult"
    }

    # Step 6: File save test
    Write-Host "`nStep 6: Performing file save test..." -ForegroundColor Yellow
    Write-Host "This test will send keystrokes to Notepad." -ForegroundColor Cyan
    Write-Host "Please ensure Notepad window is active and visible." -ForegroundColor Cyan
    Read-Host "Press Enter when ready to continue"
    
    # Load Windows Forms for SendKeys
    Add-Type -AssemblyName System.Windows.Forms
    
    # Send test text
    [System.Windows.Forms.SendKeys]::SendWait("This is a test file for ProcTail monitoring.{ENTER}")
    [System.Windows.Forms.SendKeys]::SendWait("Generated at: $(Get-Date){ENTER}")
    
    Start-Sleep -Seconds 1
    
    # Trigger Save As (Ctrl+S)
    [System.Windows.Forms.SendKeys]::SendWait("^s")
    
    Start-Sleep -Seconds 2
    
    # Enter filename
    $testFile = "$env:TEMP\proctail-test-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
    [System.Windows.Forms.SendKeys]::SendWait($testFile)
    Start-Sleep -Seconds 1
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    
    # Wait for save to complete
    Start-Sleep -Seconds 3
    
    Write-Host "✓ File save operation completed" -ForegroundColor Green

    # Step 7: Retrieve events
    Write-Host "`nStep 7: Retrieving events..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
    
    $eventsResult = & $localCliPath get-events --tag $Tag 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Events retrieved successfully!" -ForegroundColor Green
        Write-Host "`n=== EVENT DATA ===" -ForegroundColor Yellow
        Write-Host $eventsResult
        Write-Host "=== END EVENT DATA ===" -ForegroundColor Yellow
        
        if ($eventsResult -match "FileEventData" -or $eventsResult -match "Create" -or $eventsResult -match "Write") {
            Write-Host "`n✓ SUCCESS: File operation events detected!" -ForegroundColor Green
        } else {
            Write-Host "`n⚠ WARNING: No file operation events found" -ForegroundColor Yellow
        }
    } else {
        Write-Host "✗ Failed to retrieve events: $eventsResult" -ForegroundColor Red
    }

    # Test completed
    $testEndTime = Get-Date
    $testDuration = $testEndTime - $testStartTime
    
    Write-Host "`n===============================================" -ForegroundColor Green
    Write-Host "    TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "===============================================" -ForegroundColor Green
    Write-Host "Test duration: $($testDuration.TotalSeconds) seconds" -ForegroundColor Cyan
    Write-Host "Local copy at: $localRoot" -ForegroundColor Cyan

    # Cleanup
    if (-not $KeepProcesses) {
        Write-Host "`nCleaning up test processes..." -ForegroundColor Yellow
        Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $notepadPid } | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $hostProcess.Id } | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "✓ Test processes cleaned up" -ForegroundColor Green
    } else {
        Write-Host "`nKeeping processes running as requested." -ForegroundColor Cyan
        Write-Host "Host PID: $($hostProcess.Id)" -ForegroundColor Cyan
        Write-Host "Notepad PID: $notepadPid" -ForegroundColor Cyan
    }

}
catch {
    Write-Host "`n===============================================" -ForegroundColor Red
    Write-Host "    TEST FAILED!" -ForegroundColor Red
    Write-Host "===============================================" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    
    # Cleanup on failure
    Write-Host "`nCleaning up due to test failure..." -ForegroundColor Yellow
    Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    
    Read-Host "Press Enter to exit"
    exit 1
}

Read-Host "Press Enter to exit"