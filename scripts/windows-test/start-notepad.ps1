# Start Notepad for testing
# テスト用のnotepadを起動

Write-Host "Starting Notepad for testing..." -ForegroundColor Yellow

try {
    # Start notepad process
    $notepadProcess = Start-Process -FilePath "notepad.exe" -PassThru
    Write-Host "Notepad started with PID: $($notepadProcess.Id)" -ForegroundColor Green
    
    # Wait a moment for notepad to fully initialize
    Start-Sleep -Seconds 2
    
    # Verify notepad is running
    if (Get-Process -Id $notepadProcess.Id -ErrorAction SilentlyContinue) {
        Write-Host "Notepad is running successfully!" -ForegroundColor Green
        
        # Output the PID for use by other scripts
        Write-Host "NOTEPAD_PID=$($notepadProcess.Id)" -ForegroundColor Cyan
        return $notepadProcess.Id
    } else {
        Write-Host "Notepad process exited unexpectedly" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "Failed to start Notepad: $_" -ForegroundColor Red
    exit 1
}