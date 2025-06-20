# ProcTail Windows Integration Test
# Host-CLI統合テスト：ETW監視、Named Pipe通信、ファイルI/Oイベント記録の検証
param(
    [string]$Tag = "test-notepad",
    [switch]$SkipCleanup,
    [switch]$KeepProcesses
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   ProcTail Windows Integration Test" -ForegroundColor Yellow
Write-Host "   Host-CLI連携テスト (ETW + Named Pipe)" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

# Admin check
try {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
        Write-Host "Please run PowerShell as Administrator" -ForegroundColor Yellow
        Read-Host "Press Enter to exit"
        exit 1
    }
} catch {
    Write-Host "ERROR checking administrator privileges: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Administrator privileges confirmed" -ForegroundColor Green

# File path setup
$testRoot = "C:/Temp/ProcTailTest"
$hostDir = "C:/Temp/ProcTailTest/host"
$cliDir = "C:/Temp/ProcTailTest/cli"
$hostPath = "C:/Temp/ProcTailTest/host/win-x64/ProcTail.Host.exe"
$cliPath = "C:/Temp/ProcTailTest/cli/win-x64/proctail.exe"

# File existence check
if (-not (Test-Path $hostPath)) {
    Write-Host "ERROR: Host executable not found at: $hostPath" -ForegroundColor Red
    Write-Host "Please run the shell script first to build and copy files." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

if (-not (Test-Path $cliPath)) {
    Write-Host "ERROR: CLI executable not found at: $cliPath" -ForegroundColor Red
    Write-Host "Please run the shell script first to build and copy files." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Required files found" -ForegroundColor Green

# Initialize variables
$testSuccess = $false
$hostProcess = $null
$notepad = $null

# Step 1: Verify ETW Session Cleanup
Write-Host ""
Write-Host "Step 1: Verifying ETW Session Cleanup..." -ForegroundColor Cyan

# ETW cleanup should have been performed by run-windows-test.sh before file copy
Write-Host "ETW cleanup was performed before file operations to prevent locks" -ForegroundColor Gray

# Quick verification and additional cleanup if needed
$cleanupScriptPath = "C:/Temp/ProcTailScripts/cleanup-etw.ps1"
$remainingProcesses = Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue
if ($remainingProcesses) {
    Write-Host "Found remaining Host processes, performing additional cleanup..." -ForegroundColor Yellow
    if (Test-Path $cleanupScriptPath) {
        try {
            & $cleanupScriptPath -Silent | Out-Null
            Write-Host "Additional cleanup completed" -ForegroundColor Green
        }
        catch {
            # Manual fallback
            $remainingProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Seconds 2
            Write-Host "Manual process cleanup completed" -ForegroundColor Green
        }
    } else {
        $remainingProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        Write-Host "Manual process cleanup completed" -ForegroundColor Green
    }
} else {
    Write-Host "No remaining Host processes found" -ForegroundColor Green
}

Write-Host "ETW cleanup verification completed" -ForegroundColor Green

# Step 2: Start Host
Write-Host ""
Write-Host "Step 2: Starting ProcTail Host..." -ForegroundColor Cyan

try {
    Write-Host "Debug: Host path = $hostPath" -ForegroundColor Gray
    Write-Host "Debug: Working directory = $hostDir" -ForegroundColor Gray
    Write-Host "Debug: File exists = $(Test-Path $hostPath)" -ForegroundColor Gray
    
    # Try to start Host with more detailed error handling
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $hostPath
    $psi.WorkingDirectory = $hostDir
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    
    $hostProcess = [System.Diagnostics.Process]::Start($psi)
    Write-Host "Host started with PID: $($hostProcess.Id)" -ForegroundColor Green
    
    # Wait for initialization
    Write-Host "Waiting for Host initialization..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    
    # Check if Host is still running
    if ($hostProcess.HasExited) {
        Write-Host "ERROR: Host process has exited unexpectedly" -ForegroundColor Red
        Write-Host "Exit code: $($hostProcess.ExitCode)" -ForegroundColor Red
        
        # Read any error output
        $stderr = $hostProcess.StandardError.ReadToEnd()
        $stdout = $hostProcess.StandardOutput.ReadToEnd()
        
        if ($stderr) {
            Write-Host "Standard Error:" -ForegroundColor Red
            Write-Host $stderr -ForegroundColor Red
        }
        if ($stdout) {
            Write-Host "Standard Output:" -ForegroundColor Yellow
            Write-Host $stdout -ForegroundColor Yellow
        }
        
        Read-Host "Press Enter to exit"
        exit 1
    }
    
    Write-Host "Host is running" -ForegroundColor Green
}
catch {
    Write-Host "ERROR starting Host: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Exception Type: $($_.Exception.GetType().FullName)" -ForegroundColor Red
    Write-Host "Inner Exception: $($_.Exception.InnerException)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Step 3: Start Notepad and monitoring
Write-Host ""
Write-Host "Step 3: Starting Notepad and monitoring..." -ForegroundColor Cyan

try {
    $notepad = Start-Process -FilePath "notepad.exe" -PassThru
    $notepadPid = $notepad.Id
    Write-Host "Notepad started with PID: $notepadPid" -ForegroundColor Green
    
    # Add to monitoring
    Write-Host "Adding Notepad to monitoring..." -ForegroundColor Gray
    $addResult = & $cliPath add --pid $notepadPid --tag $Tag 2>&1
    Write-Host "Add result: $addResult" -ForegroundColor Gray
    
    # Check status
    Start-Sleep -Seconds 2
    $status = & $cliPath status 2>&1
    Write-Host "Status: $status" -ForegroundColor Gray
    
    Write-Host "Notepad monitoring started" -ForegroundColor Green
}
catch {
    Write-Host "ERROR with Notepad setup: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Step 4: File save test
Write-Host ""
Write-Host "Step 4: File save test..." -ForegroundColor Cyan
Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "  Please perform the following steps:" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "1. Switch to the Notepad window" -ForegroundColor White
Write-Host "2. Type some text" -ForegroundColor White
Write-Host "3. Press Ctrl+S to save" -ForegroundColor White
Write-Host "4. Choose any location and filename" -ForegroundColor White
Write-Host "5. Click Save" -ForegroundColor White
Write-Host ""

Read-Host "Press Enter after you have saved the file in Notepad"

# Step 5: Check events
Write-Host ""
Write-Host "Step 5: Checking captured events..." -ForegroundColor Cyan

Start-Sleep -Seconds 2
$events = & $cliPath events --tag $Tag 2>&1

Write-Host ""
Write-Host "================= EVENTS =================" -ForegroundColor Yellow
Write-Host $events -ForegroundColor White
Write-Host "==========================================" -ForegroundColor Yellow

# Check if events were captured
if ($events -and $events.ToString().Contains("FileIO")) {
    Write-Host "File events detected successfully!" -ForegroundColor Green
    $testSuccess = $true
} else {
    Write-Host "No file events detected" -ForegroundColor Yellow
    Write-Host "This might indicate a filtering or monitoring issue" -ForegroundColor Gray
    $testSuccess = $false
}

# Cleanup
Write-Host ""
Write-Host "Cleanup..." -ForegroundColor Cyan

if (-not $KeepProcesses) {
    # Close Notepad
    if ($notepad -and -not $notepad.HasExited) {
        $notepad | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "Notepad closed" -ForegroundColor Gray
    }
    
    # Stop Host
    if ($hostProcess -and -not $hostProcess.HasExited) {
        $hostProcess | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "Host stopped" -ForegroundColor Gray
    }
    
    # Clean ETW sessions again using dedicated script
    if (Test-Path $cleanupScriptPath) {
        Write-Host "Running final ETW cleanup..." -ForegroundColor Gray
        try {
            & $cleanupScriptPath -Silent | Out-Null
        }
        catch {
            # Fallback to manual cleanup if script fails
            $fallbackSessions = @("ProcTail", "ProcTail-Dev", "NT Kernel Logger")
            foreach ($session in $fallbackSessions) {
                try {
                    logman stop $session -ets 2>$null | Out-Null
                }
                catch {
                    # Ignore
                }
            }
        }
    }
} else {
    Write-Host "Processes kept running (--KeepProcesses flag)" -ForegroundColor Yellow
}

# Results
Write-Host ""
Write-Host "===============================================" -ForegroundColor Yellow
if ($testSuccess) {
    Write-Host "TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
} else {
    Write-Host "TEST COMPLETED WITH ISSUES" -ForegroundColor Red
}
Write-Host "===============================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "Test files are located at: $testRoot" -ForegroundColor Gray
Write-Host ""
Read-Host "Press Enter to exit"