# Test file save operations and verify events
# ファイル保存操作をテストしてイベントを確認

param(
    [string]$CliPath = "..\..\publish\cli\proctail.exe",
    [string]$Tag = "test-notepad",
    [string]$TestFile = "$env:TEMP\proctail-test.txt"
)

Write-Host "Testing file save operations and event verification..." -ForegroundColor Yellow

# Resolve full path to CLI executable
$fullCliPath = Resolve-Path $CliPath -ErrorAction SilentlyContinue
if (-not $fullCliPath) {
    Write-Host "Could not find ProcTail CLI at: $CliPath" -ForegroundColor Red
    exit 1
}

# Load Windows Forms assembly for sending keys
Add-Type -AssemblyName System.Windows.Forms

Write-Host "Performing file save test..." -ForegroundColor Cyan

try {
    # Wait a moment to ensure notepad is ready
    Start-Sleep -Seconds 1
    
    # Send test text to notepad
    Write-Host "Sending test text to notepad..." -ForegroundColor Cyan
    [System.Windows.Forms.SendKeys]::SendWait("This is a test file for ProcTail monitoring.{ENTER}")
    [System.Windows.Forms.SendKeys]::SendWait("Generated at: $(Get-Date){ENTER}")
    
    Start-Sleep -Seconds 1
    
    # Trigger Save As dialog (Ctrl+S)
    Write-Host "Triggering Save As dialog..." -ForegroundColor Cyan
    [System.Windows.Forms.SendKeys]::SendWait("^s")
    
    Start-Sleep -Seconds 2
    
    # Enter filename and save
    Write-Host "Saving file as: $TestFile" -ForegroundColor Cyan
    [System.Windows.Forms.SendKeys]::SendWait($TestFile)
    Start-Sleep -Seconds 1
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    
    # Wait for save operation to complete
    Start-Sleep -Seconds 3
    
    Write-Host "File save operation completed!" -ForegroundColor Green
}
catch {
    Write-Host "Error during file save test: $_" -ForegroundColor Red
    exit 1
}

# Wait a moment for events to be processed
Write-Host "Waiting for events to be processed..." -ForegroundColor Cyan
Start-Sleep -Seconds 2

# Retrieve events using CLI
Write-Host "Retrieving events for tag '$Tag'..." -ForegroundColor Cyan

try {
    $eventsResult = & $fullCliPath get-events --tag $Tag 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Events retrieved successfully!" -ForegroundColor Green
        Write-Host "=== EVENT DATA ===" -ForegroundColor Yellow
        Write-Host $eventsResult
        Write-Host "=== END EVENT DATA ===" -ForegroundColor Yellow
        
        # Check if we got some events
        if ($eventsResult -match "FileEventData" -or $eventsResult -match "Create" -or $eventsResult -match "Write") {
            Write-Host "SUCCESS: File operation events detected!" -ForegroundColor Green
        } else {
            Write-Host "WARNING: No file operation events found in the output" -ForegroundColor Yellow
        }
    } else {
        Write-Host "Failed to retrieve events: $eventsResult" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "Error retrieving events: $_" -ForegroundColor Red
    exit 1
}

Write-Host "File save test completed!" -ForegroundColor Green