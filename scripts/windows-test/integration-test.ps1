# ProcTail Windows Integration Test
# Host-CLI統合テスト：ETW監視、Named Pipe通信、ファイルI/Oイベント記録の検証
# test-processを使用した完全自動化テスト

# パラメータ定義
param(
    [Parameter()]
    [string]$Tag = "test-process",
    
    [Parameter()]
    [switch]$SkipCleanup,
    
    [Parameter()]
    [switch]$KeepProcesses
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   ProcTail Windows Integration Test" -ForegroundColor Yellow
Write-Host "   Host-CLI連携テスト (ETW + Named Pipe)" -ForegroundColor Yellow
Write-Host "   完全自動化テスト (test-process使用)" -ForegroundColor Yellow
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
$hostPath = "C:/Temp/ProcTailTest/host/ProcTail.Host.exe"
$cliPath = "C:/Temp/ProcTailTest/cli/proctail.exe"

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
$testProcess = $null

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

    # Create log files directory with timestamp
    $timestamp = Get-Date -Format "yyyyMMddHHmmss"
    $logDir = "C:/Temp/ProcTailTest/logs/$timestamp"
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    }
    Write-Host "Log directory: $logDir" -ForegroundColor Gray

    # Start Host process with admin privileges and redirect output to file only
    # Note: When using -Verb RunAs, -RedirectStandardOutput/Error cannot be used directly
    # We'll use a wrapper script approach that redirects to file without showing on console
    $hostLogPath = "$logDir/host.log"
    $hostErrorPath = "$logDir/host-error.log"
    
    # Create a temporary script to run the host with output redirection
    $hostWrapperScript = @"
# Redirect all output to file without showing on console
& '$hostPath' > '$hostLogPath' 2>&1
"@
    $wrapperPath = "$logDir/host-wrapper.ps1"
    $hostWrapperScript | Out-File -FilePath $wrapperPath -Encoding utf8
    
    # Start Host process with admin privileges
    $hostProcess = Start-Process pwsh.exe -ArgumentList "-WindowStyle", "Hidden", "-File", $wrapperPath -Verb RunAs -PassThru
    
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

# Step 3: Start test-process and monitoring
Write-Host ""
Write-Host "Step 3: Starting test-process and monitoring..." -ForegroundColor Cyan

$testProcessPath = "C:/Temp/ProcTailTest/tools/test-process.exe"
if (-not (Test-Path $testProcessPath)) {
    Write-Host "ERROR: test-process.exe not found at: $testProcessPath" -ForegroundColor Red
    Write-Host "Please run the shell script first to build and copy files." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

try {
    # Start test-process with continuous file operations for 30 seconds
    # Use C:/Temp/ProcTailTest/TestFiles directory instead of system temp to avoid exclusion filters
    $testFilesDir = "C:/Temp/ProcTailTest/TestFiles"
    if (-not (Test-Path $testFilesDir)) {
        New-Item -ItemType Directory -Path $testFilesDir -Force | Out-Null
    }
    
    Write-Host "Starting test-process for continuous file operations..." -ForegroundColor Gray
    $testProcessArgs = "-duration", "30s", "-interval", "2s", "-verbose", "-dir", $testFilesDir, "continuous"
    $testProcessLogPath = "$logDir/test-process.log"
    $testProcessErrorPath = "$logDir/test-process-error.log"
    $testProcess = Start-Process -FilePath $testProcessPath -ArgumentList $testProcessArgs -RedirectStandardOutput $testProcessLogPath -RedirectStandardError $testProcessErrorPath -PassThru
    if ($testProcess -and $testProcess.Id) {
        $testProcessPid = $testProcess.Id
        Write-Host "test-process started with PID: $testProcessPid" -ForegroundColor Green
        
        # Wait a moment for the process to initialize
        Start-Sleep -Seconds 3
        
        # Add to monitoring
        Write-Host "Adding test-process to monitoring..." -ForegroundColor Gray
        $addResult = & $cliPath add --pid $testProcessPid --tag $Tag 2>&1
        Write-Host "Add result: $addResult" -ForegroundColor Gray
        
        # Verify the addition was successful - increased wait time for ETW initialization
        Write-Host "Waiting for ETW monitoring to initialize..." -ForegroundColor Gray
        Start-Sleep -Seconds 5
        
        # Verify monitoring is active
        $listResult = & $cliPath list 2>&1
        Write-Host "Watch targets after addition: $listResult" -ForegroundColor Gray
        
        # Additional verification
        if ($listResult -match "$testProcessPid.*$Tag") {
            Write-Host "✓ test-process (PID: $testProcessPid) successfully added to monitoring" -ForegroundColor Green
        } else {
            Write-Host "⚠ Warning: test-process may not be properly registered for monitoring" -ForegroundColor Yellow
            Write-Host "  List output: $listResult" -ForegroundColor Yellow
        }
    } else {
        Write-Host "ERROR: Failed to start test-process" -ForegroundColor Red
        $testProcessPid = $null
        $addResult = "Failed to start test-process"
    }
    
    # Check status
    Start-Sleep -Seconds 2
    $status = & $cliPath status 2>&1
    Write-Host "Status: $status" -ForegroundColor Gray
    
    Write-Host "test-process monitoring started" -ForegroundColor Green
}
catch {
    Write-Host "ERROR with test-process setup: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Step 4: Automated file operations test
Write-Host ""
Write-Host "Step 4: Automated file operations test..." -ForegroundColor Cyan
Write-Host ""
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "  Automated Test in Progress" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Yellow
Write-Host "test-process is performing continuous file operations:" -ForegroundColor White
Write-Host "• Creating files with write operations" -ForegroundColor White
Write-Host "• Reading files to generate read events" -ForegroundColor White
Write-Host "• Deleting files to generate delete events" -ForegroundColor White
Write-Host "• Operations repeat every 2 seconds for 30 seconds" -ForegroundColor White
Write-Host ""

Write-Host "Waiting for test-process to complete its operations..." -ForegroundColor Gray

# Wait for test-process to complete (30 seconds + some buffer)
if ($testProcess) {
    $remainingTime = 35
    while ($remainingTime -gt 0 -and -not $testProcess.HasExited) {
        Write-Host "Time remaining: $remainingTime seconds" -ForegroundColor Gray
        Start-Sleep -Seconds 5
        $remainingTime -= 5
    }

    if (-not $testProcess.HasExited) {
        Write-Host "test-process is still running after 35 seconds. Terminating..." -ForegroundColor Yellow
        $testProcess | Stop-Process -Force -ErrorAction SilentlyContinue
    }
} else {
    Write-Host "test-process was not started - skipping wait" -ForegroundColor Yellow
    Start-Sleep -Seconds 5  # Still wait a bit for any potential operations
}

Write-Host "test-process operations completed" -ForegroundColor Green

# Step 5: Check events
Write-Host ""
Write-Host "Step 5: Checking captured events..." -ForegroundColor Cyan

Start-Sleep -Seconds 2
if ($testProcessPid) {
    $events = & $cliPath events --tag $Tag 2>&1
} else {
    $events = "No events to check - test-process failed to start"
}

# Save events to file for analysis
$eventsLogPath = "$logDir/events.log"
$events | Out-File -FilePath $eventsLogPath -Encoding utf8
Write-Host "Events saved to: $eventsLogPath" -ForegroundColor Gray

# Use analyze-logs.ps1 for event analysis
$analyzeScriptPath = "C:/Temp/ProcTailScripts/analyze-logs.ps1"
if (Test-Path $analyzeScriptPath) {
    Write-Host ""
    Write-Host "================= EVENT ANALYSIS =================" -ForegroundColor Cyan
    Write-Host "Running event analysis using analyze-logs.ps1..." -ForegroundColor Gray
    
    try {
        # Get JSON analysis result
        $analysisResult = & $analyzeScriptPath -EventsLogFile $eventsLogPath -OutputJson | ConvertFrom-Json
        
        if ($analysisResult.Success) {
            Write-Host "Events from test-process PID $testProcessPid analysis:" -ForegroundColor White
            Write-Host "• Total event lines found: $($analysisResult.EventLines)" -ForegroundColor White
            
            # Check file operation events
            $hasFileWrite = $analysisResult.Analysis.HasFileWrite
            $hasFileCreate = $analysisResult.Analysis.HasFileCreate  
            $hasFileDelete = $analysisResult.Analysis.HasFileDelete
            $hasTestProcessFiles = $analysisResult.Analysis.TestProcessFiles.Count -gt 0
            
            Write-Host "• FileIO/Create events: $(if ($hasFileCreate) { '✓ Found' } else { '✗ Not found' })" -ForegroundColor $(if ($hasFileCreate) { 'Green' } else { 'Red' })
            Write-Host "• FileIO/Write events: $(if ($hasFileWrite) { '✓ Found' } else { '✗ Not found' })" -ForegroundColor $(if ($hasFileWrite) { 'Green' } else { 'Red' })
            Write-Host "• FileIO/Delete events: $(if ($hasFileDelete) { '✓ Found' } else { '✗ Not found' })" -ForegroundColor $(if ($hasFileDelete) { 'Green' } else { 'Red' })
            Write-Host "• Test-process files: $(if ($hasTestProcessFiles) { '✓ Found' } else { '✗ Not found' })" -ForegroundColor $(if ($hasTestProcessFiles) { 'Green' } else { 'Red' })
            
            # Count events for this test process
            $testProcessEventCount = 0
            if ($analysisResult.ProcessEvents.PSObject.Properties[$testProcessPid]) {
                $testProcessEventCount = $analysisResult.ProcessEvents.$testProcessPid.Count
            }
            
            Write-Host "• Total events for PID $testProcessPid`: $testProcessEventCount" -ForegroundColor White
            
            # Display event type summary if any events found
            if ($analysisResult.EventTypes.PSObject.Properties.Count -gt 0) {
                Write-Host ""
                Write-Host "Event Types Summary:" -ForegroundColor Yellow
                foreach ($eventType in $analysisResult.EventTypes.PSObject.Properties) {
                    Write-Host "  $($eventType.Name): $($eventType.Value.Count) events" -ForegroundColor White
                }
            }
            
            # Test success evaluation
            if ($analysisResult.EventLines -gt 0 -and ($hasFileWrite -or $hasFileDelete -or $hasFileCreate) -and $hasTestProcessFiles) {
                Write-Host ""
                Write-Host "✅ File events detected successfully!" -ForegroundColor Green
                if ($hasFileCreate) {
                    Write-Host "✓ Create events detected" -ForegroundColor Green
                }
                if ($hasFileWrite) {
                    Write-Host "✓ Write events detected" -ForegroundColor Green
                }
                if ($hasFileDelete) {
                    Write-Host "✓ Delete events detected" -ForegroundColor Green
                }
                $testSuccess = $true
            } else {
                Write-Host ""
                Write-Host "❌ Expected file events not detected" -ForegroundColor Red
                Write-Host "This indicates a monitoring or filtering issue" -ForegroundColor Yellow
                
                # Additional diagnostic information
                if ($testProcessEventCount -eq 0) {
                    Write-Host "⚠️  No events found for test-process PID $testProcessPid" -ForegroundColor Yellow
                    Write-Host "   This suggests the process monitoring registration failed" -ForegroundColor Gray
                } else {
                    Write-Host "ℹ️  Found $testProcessEventCount events for PID $testProcessPid but they don't match expected file operations" -ForegroundColor Yellow
                }
                
                $testSuccess = $false
            }
            
        } else {
            Write-Host "❌ Analysis failed: $($analysisResult.Error)" -ForegroundColor Red
            $testSuccess = $false
        }
        
    } catch {
        Write-Host "❌ Error running event analysis: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Falling back to basic analysis..." -ForegroundColor Yellow
        
        # Fallback to original analysis
        $eventString = $events.ToString()
        $hasFileWrite = $eventString.Contains("FileIO") -and $eventString.Contains("Write")
        $hasFileDelete = $eventString.Contains("FileIO") -and $eventString.Contains("Delete") 
        $hasFileCreate = $eventString.Contains("FileIO") -and $eventString.Contains("Create")
        $hasTestProcessFiles = $eventString.Contains("continuous_$testProcessPid") -or $eventString.Contains("TestFiles")
        
        if ($events -and ($hasFileWrite -or $hasFileDelete -or $hasFileCreate) -and $hasTestProcessFiles) {
            $testSuccess = $true
        } else {
            $testSuccess = $false
        }
    }
    
} else {
    Write-Host "❌ analyze-logs.ps1 not found at: $analyzeScriptPath" -ForegroundColor Red
    Write-Host "Falling back to basic analysis..." -ForegroundColor Yellow
    
    # Fallback to original analysis
    $eventString = $events.ToString()
    $hasFileWrite = $eventString.Contains("FileIO") -and $eventString.Contains("Write")
    $hasFileDelete = $eventString.Contains("FileIO") -and $eventString.Contains("Delete") 
    $hasFileCreate = $eventString.Contains("FileIO") -and $eventString.Contains("Create")
    $hasTestProcessFiles = $eventString.Contains("continuous_$testProcessPid") -or $eventString.Contains("TestFiles")
    
    if ($events -and ($hasFileWrite -or $hasFileDelete -or $hasFileCreate) -and $hasTestProcessFiles) {
        $testSuccess = $true
    } else {
        $testSuccess = $false
    }
}

Write-Host "=================================================" -ForegroundColor Cyan

# Display log files for debugging
Write-Host ""
Write-Host "================= DEBUG LOGS =================" -ForegroundColor Cyan

# Check test-process log
if (Test-Path $testProcessLogPath) {
    Write-Host ""
    Write-Host "test-process log:" -ForegroundColor Yellow
    Get-Content $testProcessLogPath -Encoding "utf8" | ForEach-Object { Write-Host $_ -ForegroundColor White }
} else {
    Write-Host "test-process log file not found: $testProcessLogPath" -ForegroundColor Red
}

# Check test-process error log  
if (Test-Path $testProcessErrorPath) {
    Write-Host ""
    Write-Host "test-process error log:" -ForegroundColor Yellow
    Get-Content $testProcessErrorPath -Encoding "utf8" | ForEach-Object { Write-Host $_ -ForegroundColor White }
}

# # Manual test to verify test-process can create files
# Write-Host ""
# Write-Host "Manual test-process verification:" -ForegroundColor Yellow
# if ($testProcessPid) {
#     Write-Host "Running a simple file-write test to verify test-process functionality..." -ForegroundColor Gray
#     $manualTestArgs = "-count", "3", "-verbose", "-dir", $testFilesDir, "file-write"
#     try {
#         $manualResult = & $testProcessPath $manualTestArgs 2>&1
#         Write-Host "Manual test result:" -ForegroundColor Gray
#         $manualResult | ForEach-Object { Write-Host "  $_" -ForegroundColor White }
        
#         # Check if files were created by manual test
#         Start-Sleep -Seconds 2
#         $manualFiles = Get-ChildItem $testFilesDir -ErrorAction SilentlyContinue
#         if ($manualFiles) {
#             Write-Host "Manual test created files:" -ForegroundColor Green
#             $manualFiles | ForEach-Object { Write-Host "  $($_.Name)" -ForegroundColor Green }
#         } else {
#             Write-Host "Manual test failed to create files" -ForegroundColor Red
#         }
#     } catch {
#         Write-Host "Manual test failed: $($_.Exception.Message)" -ForegroundColor Red
#     }
# }

# Check Host process logs (summary only)
Write-Host ""
Write-Host "Host Process Status:" -ForegroundColor Yellow
$hostLogPath = "$logDir/host.log"
if (Test-Path $hostLogPath) {
    $hostLogSize = (Get-Item $hostLogPath).Length
    $hostLogSizeKB = [math]::Round($hostLogSize / 1KB, 2)
    Write-Host "  Host log saved to: $hostLogPath ($hostLogSizeKB KB)" -ForegroundColor Gray
    
    # Check for errors in the log
    $hostLogContent = Get-Content $hostLogPath -Raw
    $errorCount = ([regex]::Matches($hostLogContent, "ERROR|Exception|Failed")).Count
    $warningCount = ([regex]::Matches($hostLogContent, "WARN|Warning")).Count
    
    if ($errorCount -gt 0) {
        Write-Host "  ⚠ Found $errorCount error(s) in Host log" -ForegroundColor Yellow
    }
    if ($warningCount -gt 0) {
        Write-Host "  ⚠ Found $warningCount warning(s) in Host log" -ForegroundColor Yellow
    }
    if ($errorCount -eq 0 -and $warningCount -eq 0) {
        Write-Host "  ✓ No errors or warnings found in Host log" -ForegroundColor Green
    }
} else {
    Write-Host "  Host log file not found: $hostLogPath" -ForegroundColor Red
}

Write-Host "=============================================" -ForegroundColor Cyan

# Cleanup
Write-Host ""
Write-Host "Cleanup..." -ForegroundColor Cyan

if (-not $KeepProcesses) {
    # Close test-process
    if ($testProcess -and -not $testProcess.HasExited) {
        $testProcess | Stop-Process -Force -ErrorAction SilentlyContinue
        Write-Host "test-process closed" -ForegroundColor Gray
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
Write-Host "Log files are located at: $logDir" -ForegroundColor Gray
Write-Host ""
Read-Host "Press Enter to exit"