# Deep debug for event detection issues
# イベント検出問題の詳細デバッグ

Write-Host "Deep Debug for Event Detection" -ForegroundColor Yellow
Write-Host "===============================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

$localCliPath = "C:\Temp\ProcTailTest\cli\proctail.exe"
$configPath = "C:\Temp\ProcTailTest\host\appsettings.json"

# 1. Check current configuration
Write-Host "`n1. Checking current configuration..." -ForegroundColor Cyan
if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
    Write-Host "MinimumProcessId: $($config.ETW.Filtering.MinimumProcessId)" -ForegroundColor Gray
    Write-Host "ExcludeFilePatterns count: $($config.ETW.Filtering.ExcludeFilePatterns.Count)" -ForegroundColor Gray
    
    $hasTempPattern = $config.ETW.Filtering.ExcludeFilePatterns -contains "*\Temp\*"
    if ($hasTempPattern) {
        Write-Host "❌ *\Temp\* pattern still exists!" -ForegroundColor Red
    } else {
        Write-Host "✅ *\Temp\* pattern removed" -ForegroundColor Green
    }
} else {
    Write-Host "❌ Configuration file not found" -ForegroundColor Red
}

# 2. Check Host logs for recent activity
Write-Host "`n2. Checking recent Host logs..." -ForegroundColor Cyan
$logFiles = Get-ChildItem "C:\ProcTail-Test-Logs\host-*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($logFiles) {
    Write-Host "Latest log: $($logFiles.FullName)" -ForegroundColor Gray
    $recentLogs = Get-Content $logFiles.FullName -Tail 20
    Write-Host "Recent log entries:" -ForegroundColor Yellow
    foreach ($line in $recentLogs) {
        if ($line -match "2688" -or $line -match "FileIO" -or $line -match "イベント") {
            Write-Host "  $line" -ForegroundColor White
        } else {
            Write-Host "  $line" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "❌ No log files found" -ForegroundColor Red
}

# 3. Start new notepad and test immediately
Write-Host "`n3. Starting fresh notepad test..." -ForegroundColor Cyan
$notepad = Start-Process -FilePath "notepad.exe" -PassThru
$notepadPid = $notepad.Id
Write-Host "New notepad PID: $notepadPid" -ForegroundColor Green

# Add to monitoring
Write-Host "Adding to monitoring..." -ForegroundColor Gray
$addResult = & $localCliPath add --pid $notepadPid --tag "deep-debug" 2>&1
Write-Host "Add result: $addResult" -ForegroundColor Gray

# Wait and check status
Start-Sleep -Seconds 2
$status = & $localCliPath status 2>&1
Write-Host "Status: $status" -ForegroundColor Gray

# 4. Create a test file in Desktop instead of Temp
Write-Host "`n4. Testing with Desktop file (not Temp)..." -ForegroundColor Cyan
$desktopPath = [Environment]::GetFolderPath("Desktop")
$testFile = Join-Path $desktopPath "proctail-test-$(Get-Date -Format 'HHmmss').txt"
Write-Host "Test file: $testFile" -ForegroundColor Gray

Write-Host "Instructions:" -ForegroundColor Yellow
Write-Host "1. Switch to notepad" -ForegroundColor White
Write-Host "2. Type some text" -ForegroundColor White
Write-Host "3. Press Ctrl+S" -ForegroundColor White
Write-Host "4. Save as: $testFile" -ForegroundColor White
Write-Host "5. Press Enter to save" -ForegroundColor White

Read-Host "Press Enter when you've completed the save operation"

# 5. Check events immediately
Write-Host "`n5. Checking events immediately..." -ForegroundColor Cyan
$events = & $localCliPath events --tag "deep-debug" 2>&1
Write-Host "Events result: $events" -ForegroundColor White

# 6. Check if file was created
Write-Host "`n6. Verifying file creation..." -ForegroundColor Cyan
if (Test-Path $testFile) {
    $fileInfo = Get-Item $testFile
    Write-Host "✅ File created: $($fileInfo.FullName)" -ForegroundColor Green
    Write-Host "   Size: $($fileInfo.Length) bytes" -ForegroundColor Gray
    Write-Host "   Modified: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
} else {
    Write-Host "❌ File not found: $testFile" -ForegroundColor Red
}

# 7. Check all events for any tag
Write-Host "`n7. Checking all events..." -ForegroundColor Cyan
$allEvents = & $localCliPath events 2>&1
Write-Host "All events: $allEvents" -ForegroundColor White

# 8. Final log check
Write-Host "`n8. Final log check..." -ForegroundColor Cyan
if ($logFiles) {
    $finalLogs = Get-Content $logFiles.FullName -Tail 50
    $relevantLogs = $finalLogs | Where-Object { $_ -match $notepadPid -or $_ -match "FileEventData" -or $_ -match "イベントを記録" }
    if ($relevantLogs) {
        Write-Host "Relevant log entries:" -ForegroundColor Yellow
        foreach ($log in $relevantLogs) {
            Write-Host "  $log" -ForegroundColor White
        }
    } else {
        Write-Host "No relevant log entries found for PID $notepadPid" -ForegroundColor Yellow
    }
}

# Cleanup
Write-Host "`n9. Cleaning up..." -ForegroundColor Cyan
if (Test-Path $testFile) {
    Remove-Item $testFile -Force -ErrorAction SilentlyContinue
    Write-Host "Test file removed" -ForegroundColor Gray
}

$notepad | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "Notepad closed" -ForegroundColor Gray

Read-Host "`nPress Enter to exit"