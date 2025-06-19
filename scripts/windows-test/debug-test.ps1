# Debug version of Windows Integration Test with comprehensive logging
# 詳細ログ付きWindows統合テストのデバッグ版

param(
    [string]$Tag = "debug-test",
    [switch]$SkipCleanup,
    [switch]$KeepProcesses
)

$ErrorActionPreference = "Continue"
$VerbosePreference = "Continue"

# Create log directory
$logDir = "C:\ProcTail-Test-Logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$logFile = "$logDir\test-debug-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
$hostLogFile = "$logDir\host-debug-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    $logEntry = "[$timestamp] [$Level] $Message"
    Write-Host $logEntry
    Add-Content -Path $logFile -Value $logEntry
}

function Test-AdminRights {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

Write-Log "===============================================" "INFO"
Write-Log "ProcTail Windows Integration Test - DEBUG MODE" "INFO"
Write-Log "===============================================" "INFO"
Write-Log "Log file: $logFile" "INFO"
Write-Log "Host log file: $hostLogFile" "INFO"

$scriptDir = $PSScriptRoot
$testStartTime = Get-Date
Write-Log "Script directory: $scriptDir" "INFO"
Write-Log "Test start time: $testStartTime" "INFO"

# Check admin rights
if (-not (Test-AdminRights)) {
    Write-Log "ERROR: This test requires administrator privileges." "ERROR"
    Write-Log "Please run PowerShell as Administrator and try again." "ERROR"
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Log "Administrator privileges confirmed" "INFO"

try {
    # Step 1: Cleanup
    if (-not $SkipCleanup) {
        Write-Log "Step 1: Starting cleanup..." "INFO"
        try {
            Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | ForEach-Object {
                Write-Log "Killing existing ProcTail.Host process (PID: $($_.Id))" "INFO"
                $_ | Stop-Process -Force -ErrorAction SilentlyContinue
            }
            
            # Stop ETW sessions
            $etwSessions = @("ProcTail", "NT Kernel Logger")
            foreach ($session in $etwSessions) {
                try {
                    $result = logman stop $session -ets 2>&1
                    Write-Log "ETW session stop result for $session`: $result" "INFO"
                }
                catch {
                    Write-Log "ETW session stop error for $session`: $_" "WARN"
                }
            }
            
            Start-Sleep -Seconds 2
            Write-Log "Cleanup completed" "INFO"
        }
        catch {
            Write-Log "Cleanup error: $_" "ERROR"
        }
    }

    # Step 2: Start Host
    Write-Log "Step 2: Starting ProcTail Host..." "INFO"
    $hostPath = Join-Path $scriptDir "..\..\publish\host\ProcTail.Host.exe"
    $resolvedHostPath = Resolve-Path $hostPath -ErrorAction SilentlyContinue
    
    if (-not $resolvedHostPath) {
        Write-Log "ERROR: Could not find ProcTail Host at: $hostPath" "ERROR"
        throw "Host executable not found"
    }
    
    Write-Log "Host path resolved: $resolvedHostPath" "INFO"
    
    try {
        # Start host and redirect output to log file
        $hostProcessArgs = @{
            FilePath = $resolvedHostPath
            PassThru = $true
            WindowStyle = "Hidden"
            RedirectStandardOutput = $true
            RedirectStandardError = $true
            UseNewEnvironment = $false
        }
        
        Write-Log "Starting host process..." "INFO"
        $hostProcess = Start-Process @hostProcessArgs
        
        Write-Log "Host started with PID: $($hostProcess.Id)" "INFO"
        
        # Wait for host to initialize
        Start-Sleep -Seconds 5
        
        # Check if process is still running
        $runningProcess = Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue
        if ($runningProcess) {
            Write-Log "Host is running successfully!" "INFO"
        } else {
            Write-Log "Host process exited unexpectedly" "ERROR"
            throw "Host process failed to start or exited"
        }
    }
    catch {
        Write-Log "Failed to start Host: $_" "ERROR"
        throw
    }

    # Step 3: Start Notepad
    Write-Log "Step 3: Starting Notepad..." "INFO"
    try {
        $notepadProcess = Start-Process -FilePath "notepad.exe" -PassThru
        Write-Log "Notepad started with PID: $($notepadProcess.Id)" "INFO"
        
        Start-Sleep -Seconds 2
        
        if (Get-Process -Id $notepadProcess.Id -ErrorAction SilentlyContinue) {
            Write-Log "Notepad is running successfully!" "INFO"
            $notepadPid = $notepadProcess.Id
        } else {
            Write-Log "Notepad process exited unexpectedly" "ERROR"
            throw "Notepad failed to start"
        }
    }
    catch {
        Write-Log "Failed to start Notepad: $_" "ERROR"
        throw
    }

    # Step 4: Start CLI and subscribe
    Write-Log "Step 4: Starting CLI and subscribing to Notepad (PID: $notepadPid)..." "INFO"
    $cliPath = Join-Path $scriptDir "..\..\publish\cli\proctail.exe"
    $resolvedCliPath = Resolve-Path $cliPath -ErrorAction SilentlyContinue
    
    if (-not $resolvedCliPath) {
        Write-Log "ERROR: Could not find ProcTail CLI at: $cliPath" "ERROR"
        throw "CLI executable not found"
    }
    
    Write-Log "CLI path resolved: $resolvedCliPath" "INFO"
    
    try {
        Write-Log "Executing: $resolvedCliPath add-watch-target --pid $notepadPid --tag $Tag" "INFO"
        $addResult = & $resolvedCliPath add-watch-target --pid $notepadPid --tag $Tag 2>&1
        
        Write-Log "CLI add-watch-target exit code: $LASTEXITCODE" "INFO"
        Write-Log "CLI add-watch-target output: $addResult" "INFO"
        
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Successfully added watch target for notepad!" "INFO"
        } else {
            Write-Log "Failed to add watch target" "ERROR"
            throw "CLI add-watch-target failed with exit code $LASTEXITCODE"
        }
    }
    catch {
        Write-Log "Error running CLI add-watch-target: $_" "ERROR"
        throw
    }

    # Step 5: Manual file save test (simplified)
    Write-Log "Step 5: Manual file save test..." "INFO"
    Write-Log "Please manually:" "INFO"
    Write-Log "1. Switch to the Notepad window" "INFO"
    Write-Log "2. Type some text" "INFO"
    Write-Log "3. Save the file (Ctrl+S)" "INFO"
    Write-Log "4. Choose a location and save" "INFO"
    Read-Host "Press Enter when you have completed the file save operation"

    # Step 6: Retrieve events
    Write-Log "Step 6: Retrieving events..." "INFO"
    Start-Sleep -Seconds 2
    
    try {
        Write-Log "Executing: $resolvedCliPath get-events --tag $Tag" "INFO"
        $eventsResult = & $resolvedCliPath get-events --tag $Tag 2>&1
        
        Write-Log "CLI get-events exit code: $LASTEXITCODE" "INFO"
        Write-Log "CLI get-events output length: $($eventsResult.Length) characters" "INFO"
        Write-Log "=== EVENT DATA START ===" "INFO"
        Write-Log "$eventsResult" "INFO"
        Write-Log "=== EVENT DATA END ===" "INFO"
        
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Events retrieved successfully!" "INFO"
            
            if ($eventsResult -match "FileEventData" -or $eventsResult -match "Create" -or $eventsResult -match "Write") {
                Write-Log "SUCCESS: File operation events detected!" "INFO"
            } else {
                Write-Log "WARNING: No file operation events found in the output" "WARN"
            }
        } else {
            Write-Log "Failed to retrieve events" "ERROR"
        }
    }
    catch {
        Write-Log "Error retrieving events: $_" "ERROR"
    }

    # Test completed
    $testEndTime = Get-Date
    $testDuration = $testEndTime - $testStartTime
    
    Write-Log "===============================================" "INFO"
    Write-Log "TEST COMPLETED" "INFO"
    Write-Log "===============================================" "INFO"
    Write-Log "Test duration: $($testDuration.TotalSeconds) seconds" "INFO"
    Write-Log "Tag used: $Tag" "INFO"
    Write-Log "Notepad PID: $notepadPid" "INFO"

    # Cleanup
    if (-not $KeepProcesses) {
        Write-Log "Cleaning up test processes..." "INFO"
        Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Where-Object { $_.Id -eq $notepadPid } | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Log "Test processes cleaned up." "INFO"
    }

}
catch {
    Write-Log "===============================================" "ERROR"
    Write-Log "TEST FAILED!" "ERROR"
    Write-Log "===============================================" "ERROR"
    Write-Log "Error: $_" "ERROR"
    Write-Log "Stack trace: $($_.ScriptStackTrace)" "ERROR"
    
    # Cleanup on failure
    Write-Log "Cleaning up due to test failure..." "INFO"
    Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Get-Process -Name "notepad" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    
    exit 1
}
finally {
    Write-Log "Log file saved to: $logFile" "INFO"
    Read-Host "Press Enter to exit"
}