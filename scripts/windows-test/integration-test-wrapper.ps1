# Wrapper script to safely execute integration-test.ps1
try {
    $scriptPath = Join-Path $PSScriptRoot "integration-test.ps1"
    if (Test-Path $scriptPath) {
        & $scriptPath
    } else {
        Write-Host "ERROR: integration-test.ps1 not found at: $scriptPath" -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
}
catch {
    Write-Host "ERROR executing integration test:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "" -ForegroundColor Red
    Write-Host "Stack Trace:" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}