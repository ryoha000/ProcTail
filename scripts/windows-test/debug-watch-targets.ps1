# Debug watch targets and event filtering
# 監視対象とイベントフィルタリングのデバッグ

Write-Host "Debug Watch Targets" -ForegroundColor Yellow
Write-Host "===================" -ForegroundColor Yellow

$localCliPath = "C:\Temp\ProcTailTest\cli\proctail.exe"

if (-not (Test-Path $localCliPath)) {
    Write-Host "CLI not found. Run the test first." -ForegroundColor Red
    exit 1
}

# Check current status
Write-Host "`nCurrent ProcTail status:" -ForegroundColor Cyan
$status = & $localCliPath status 2>&1
Write-Host $status

# List current watch targets
Write-Host "`nCurrent watch targets:" -ForegroundColor Cyan
$targets = & $localCliPath list 2>&1
Write-Host $targets

# Start a new notepad for testing
Write-Host "`nStarting a new notepad for testing..." -ForegroundColor Cyan
$notepad = Start-Process -FilePath "notepad.exe" -PassThru
$notepadPid = $notepad.Id
Write-Host "New notepad PID: $notepadPid" -ForegroundColor Green

# Add watch target
Write-Host "`nAdding watch target..." -ForegroundColor Cyan
$addResult = & $localCliPath add --pid $notepadPid --tag "debug-test" 2>&1
Write-Host "Add result: $addResult"

# Check status again
Write-Host "`nStatus after adding target:" -ForegroundColor Cyan
$statusAfter = & $localCliPath status 2>&1
Write-Host $statusAfter

# List targets again
Write-Host "`nWatch targets after adding:" -ForegroundColor Cyan
$targetsAfter = & $localCliPath list 2>&1
Write-Host $targetsAfter

# Do a simple file operation
Write-Host "`nPerforming file operation..." -ForegroundColor Cyan
Write-Host "Please switch to notepad, type some text, and save a file." -ForegroundColor Yellow
Read-Host "Press Enter when done"

# Check events
Write-Host "`nChecking events..." -ForegroundColor Cyan
$events = & $localCliPath events --tag "debug-test" 2>&1
Write-Host "Events:"
Write-Host $events

Read-Host "Press Enter to exit"