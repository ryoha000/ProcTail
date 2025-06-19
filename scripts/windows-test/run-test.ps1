# Integrated Windows Test Runner for ProcTail
# ProcTail Windows機能統合テストランナー

param(
    [string]$Tag = "test-notepad",
    [switch]$SkipCleanup,
    [switch]$KeepProcesses
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "    ProcTail Windows Integration Test" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

$scriptDir = $PSScriptRoot
$testStartTime = Get-Date

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Write-Host "Please run PowerShell as Administrator and try again." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Function to run script and capture result
function Invoke-TestScript {
    param(
        [string]$ScriptPath,
        [string]$Arguments = "",
        [string]$Description
    )
    
    Write-Host "`n--- $Description ---" -ForegroundColor Cyan
    try {
        if ($Arguments) {
            $result = & $ScriptPath $Arguments.Split(' ') 2>&1
        } else {
            $result = & $ScriptPath 2>&1
        }
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ $Description completed successfully" -ForegroundColor Green
            return $result
        } else {
            Write-Host "✗ $Description failed" -ForegroundColor Red
            Write-Host $result
            return $null
        }
    }
    catch {
        Write-Host "✗ $Description failed with exception: $_" -ForegroundColor Red
        return $null
    }
}

try {
    # Step 1: Cleanup existing processes
    if (-not $SkipCleanup) {
        Write-Host "`nStep 1: Cleaning up existing processes..." -ForegroundColor Yellow
        Invoke-TestScript -ScriptPath "$scriptDir\cleanup.ps1" -Description "Cleanup existing processes"
    } else {
        Write-Host "`nStep 1: Skipping cleanup (as requested)" -ForegroundColor Yellow
    }

    # Step 2: Start Host
    Write-Host "`nStep 2: Starting ProcTail Host..." -ForegroundColor Yellow
    $hostResult = Invoke-TestScript -ScriptPath "$scriptDir\start-host.ps1" -Description "Start ProcTail Host"
    if (-not $hostResult) {
        throw "Failed to start Host"
    }

    # Step 3: Start Notepad
    Write-Host "`nStep 3: Starting Notepad..." -ForegroundColor Yellow
    $notepadResult = Invoke-TestScript -ScriptPath "$scriptDir\start-notepad.ps1" -Description "Start Notepad"
    if (-not $notepadResult) {
        throw "Failed to start Notepad"
    }
    
    # Extract PID from notepad result
    $notepadPid = $null
    foreach ($line in $notepadResult) {
        if ($line -match "NOTEPAD_PID=(\d+)") {
            $notepadPid = $matches[1]
            break
        }
        if ($line -match "Notepad started with PID: (\d+)") {
            $notepadPid = $matches[1]
            break
        }
    }
    
    if (-not $notepadPid) {
        throw "Could not determine Notepad PID"
    }
    
    Write-Host "Using Notepad PID: $notepadPid" -ForegroundColor Cyan

    # Step 4: Start CLI and subscribe to notepad
    Write-Host "`nStep 4: Starting CLI and subscribing to Notepad..." -ForegroundColor Yellow
    $cliResult = Invoke-TestScript -ScriptPath "$scriptDir\start-cli.ps1" -Arguments "-NotepadPid $notepadPid -Tag $Tag" -Description "Start CLI and subscribe to Notepad"
    if (-not $cliResult) {
        throw "Failed to start CLI and subscribe to Notepad"
    }

    # Step 5: Perform file save test
    Write-Host "`nStep 5: Performing file save test..." -ForegroundColor Yellow
    Write-Host "IMPORTANT: The test will now send keystrokes to Notepad." -ForegroundColor Yellow
    Write-Host "Please ensure Notepad window is active and visible." -ForegroundColor Yellow
    Read-Host "Press Enter when ready to continue"
    
    $saveResult = Invoke-TestScript -ScriptPath "$scriptDir\file-save-test.ps1" -Arguments "-Tag $Tag" -Description "File save test and event verification"
    if (-not $saveResult) {
        throw "Failed to perform file save test"
    }

    # Test completed successfully
    $testEndTime = Get-Date
    $testDuration = $testEndTime - $testStartTime
    
    Write-Host "`n===============================================" -ForegroundColor Green
    Write-Host "    TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
    Write-Host "===============================================" -ForegroundColor Green
    Write-Host "Test duration: $($testDuration.TotalSeconds) seconds" -ForegroundColor Cyan
    Write-Host "Tag used: $Tag" -ForegroundColor Cyan
    Write-Host "Notepad PID: $notepadPid" -ForegroundColor Cyan

    # Cleanup unless requested to keep processes
    if (-not $KeepProcesses) {
        Write-Host "`nCleaning up test processes..." -ForegroundColor Yellow
        Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $notepadPid } | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "Test processes cleaned up." -ForegroundColor Green
    } else {
        Write-Host "`nKeeping processes running as requested." -ForegroundColor Cyan
        Write-Host "Notepad PID: $notepadPid (still running)" -ForegroundColor Cyan
    }

    Write-Host "`nTo view events again, run:" -ForegroundColor Cyan
    Write-Host "  .\publish\cli\proctail.exe get-events --tag $Tag" -ForegroundColor White

}
catch {
    Write-Host "`n===============================================" -ForegroundColor Red
    Write-Host "    TEST FAILED!" -ForegroundColor Red
    Write-Host "===============================================" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    
    # Cleanup on failure
    Write-Host "`nCleaning up due to test failure..." -ForegroundColor Yellow
    & "$scriptDir\cleanup.ps1"
    
    Read-Host "Press Enter to exit"
    exit 1
}

Read-Host "Press Enter to exit"