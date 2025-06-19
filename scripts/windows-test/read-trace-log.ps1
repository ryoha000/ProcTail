# Read and analyze trace log
$logPath = "C:\ProcTail-Test-Logs\host-trace-20250620-042512.log"
if (Test-Path $logPath) {
    $content = Get-Content $logPath
    Write-Host "=== TRACE LOG CONTENT ===" -ForegroundColor Yellow
    $content | ForEach-Object { Write-Host $_ }
    
    Write-Host "`n=== ERROR ANALYSIS ===" -ForegroundColor Yellow
    $errors = $content | Where-Object { $_ -match "error|fail|exception" }
    if ($errors) {
        Write-Host "Found errors:" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host $_ -ForegroundColor Red }
    } else {
        Write-Host "No explicit errors found in trace" -ForegroundColor Green
    }
}