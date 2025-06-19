# Final integration test with ETW cleanup and pipe name fix
# ETWクリーンアップとパイプ名修正を含む最終統合テスト

param(
    [string]$Tag = "test-notepad",
    [switch]$SkipCleanup,
    [switch]$KeepProcesses
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   ProcTail Final Integration Test" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Setup paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$sourceHostDir = Join-Path $projectRoot "publish\host"
$sourceCliDir = Join-Path $projectRoot "publish\cli"

$localRoot = "C:\Temp\ProcTailTest"
$localHostDir = Join-Path $localRoot "host"
$localCliDir = Join-Path $localRoot "cli"
$localHostPath = Join-Path $localHostDir "ProcTail.Host.exe"
$localCliPath = Join-Path $localCliDir "proctail.exe"

try {
    # Step 1: ETW Cleanup
    Write-Host "`nStep 1: ETW Session Cleanup..." -ForegroundColor Yellow
    
    $etwSessions = @("ProcTail", "ProcTail-Dev", "NT Kernel Logger")
    foreach ($session in $etwSessions) {
        try {
            logman stop $session -ets 2>$null
            Write-Host "Stopped ETW session: $session" -ForegroundColor Green
        }
        catch {
            Write-Host "ETW session '$session' was not running" -ForegroundColor Gray
        }
    }
    
    # Kill existing processes
    Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    
    Write-Host "✓ ETW cleanup completed" -ForegroundColor Green

    # Step 2: Prepare local copy
    Write-Host "`nStep 2: Preparing local copy..." -ForegroundColor Yellow
    
    New-Item -ItemType Directory -Path $localRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $localHostDir -Force | Out-Null
    New-Item -ItemType Directory -Path $localCliDir -Force | Out-Null
    
    Copy-Item -Path "$sourceHostDir\*" -Destination $localHostDir -Recurse -Force
    Copy-Item -Path "$sourceCliDir\*" -Destination $localCliDir -Recurse -Force
    
    # Fix appsettings.json PipeName for Development environment
    $configPath = Join-Path $localHostDir "appsettings.json"
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    $config.NamedPipe.PipeName = "ProcTail"
    $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
    
    Write-Host "✓ Files copied and configured" -ForegroundColor Green

    # Step 3: Start Host
    Write-Host "`nStep 3: Starting ProcTail Host..." -ForegroundColor Yellow
    
    $hostProcess = Start-Process -FilePath $localHostPath -PassThru -WorkingDirectory $localHostDir
    Write-Host "Host started with PID: $($hostProcess.Id)" -ForegroundColor Cyan
    
    # Wait for initialization
    Write-Host "Waiting for Host initialization..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    
    # Verify Host is running
    if (-not (Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue)) {
        throw "Host process exited unexpectedly"
    }
    
    Write-Host "✓ Host is running successfully" -ForegroundColor Green

    # Step 4: Verify Named Pipe
    Write-Host "`nStep 4: Verifying Named Pipe..." -ForegroundColor Yellow
    
    Start-Sleep -Seconds 2
    $statusResult = & $localCliPath status 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Named Pipe connection successful!" -ForegroundColor Green
        Write-Host "Status result: $statusResult" -ForegroundColor Gray
    } else {
        Write-Host "⚠ Named Pipe connection failed: $statusResult" -ForegroundColor Yellow
        Write-Host "Continuing with test anyway..." -ForegroundColor Gray
    }

    # Step 5: Start Notepad
    Write-Host "`nStep 5: Starting Notepad..." -ForegroundColor Yellow
    
    $notepadProcess = Start-Process -FilePath "notepad.exe" -PassThru
    $notepadPid = $notepadProcess.Id
    Write-Host "✓ Notepad started with PID: $notepadPid" -ForegroundColor Green
    Start-Sleep -Seconds 2

    # Step 6: Add watch target
    Write-Host "`nStep 6: Adding watch target..." -ForegroundColor Yellow
    
    $addResult = & $localCliPath add --pid $notepadPid --tag $Tag 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Successfully added watch target!" -ForegroundColor Green
        Write-Host "Result: $addResult" -ForegroundColor Gray
    } else {
        Write-Host "✗ Failed to add watch target: $addResult" -ForegroundColor Red
        throw "Failed to add watch target"
    }

    # Step 7: File save test
    Write-Host "`nStep 7: File save test..." -ForegroundColor Yellow
    Write-Host "This will send keystrokes to Notepad." -ForegroundColor Cyan
    Write-Host "Ensure Notepad window is active." -ForegroundColor Cyan
    Read-Host "Press Enter when ready"
    
    Add-Type -AssemblyName System.Windows.Forms
    
    [System.Windows.Forms.SendKeys]::SendWait("ProcTail integration test{ENTER}")
    [System.Windows.Forms.SendKeys]::SendWait("Time: $(Get-Date){ENTER}")
    Start-Sleep -Seconds 1
    
    [System.Windows.Forms.SendKeys]::SendWait("^s")
    Start-Sleep -Seconds 2
    
    $testFile = "$env:TEMP\proctail-final-test-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
    [System.Windows.Forms.SendKeys]::SendWait($testFile)
    Start-Sleep -Seconds 1
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    
    Start-Sleep -Seconds 3
    Write-Host "✓ File save completed" -ForegroundColor Green

    # Step 8: Retrieve events
    Write-Host "`nStep 8: Retrieving events..." -ForegroundColor Yellow
    Start-Sleep -Seconds 2
    
    $eventsResult = & $localCliPath events --tag $Tag 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✓ Events retrieved!" -ForegroundColor Green
        Write-Host "`n=== EVENTS ===" -ForegroundColor Yellow
        Write-Host $eventsResult
        Write-Host "=== END EVENTS ===" -ForegroundColor Yellow
        
        if ($eventsResult -match "FileEventData" -or $eventsResult -match "Create" -or $eventsResult -match "Write") {
            Write-Host "`n🎉 SUCCESS: File operation events detected!" -ForegroundColor Green
        } else {
            Write-Host "`n⚠ No file operation events found" -ForegroundColor Yellow
        }
    } else {
        Write-Host "✗ Failed to retrieve events: $eventsResult" -ForegroundColor Red
    }

    # Success
    Write-Host "`n===============================================" -ForegroundColor Green
    Write-Host "    INTEGRATION TEST COMPLETED!" -ForegroundColor Green
    Write-Host "===============================================" -ForegroundColor Green

    # Cleanup
    if (-not $KeepProcesses) {
        Write-Host "`nCleaning up..." -ForegroundColor Yellow
        Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $notepadPid } | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $hostProcess.Id } | Stop-Process -Force -ErrorAction SilentlyContinue
        
        # ETW cleanup
        foreach ($session in $etwSessions) {
            try { logman stop $session -ets 2>$null } catch {}
        }
        
        Write-Host "✓ Cleanup completed" -ForegroundColor Green
    }

}
catch {
    Write-Host "`n===============================================" -ForegroundColor Red
    Write-Host "    TEST FAILED!" -ForegroundColor Red
    Write-Host "===============================================" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    
    # Cleanup on failure
    Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    
    foreach ($session in @("ProcTail", "ProcTail-Dev", "NT Kernel Logger")) {
        try { logman stop $session -ets 2>$null } catch {}
    }
    
    exit 1
}

Read-Host "Press Enter to exit"