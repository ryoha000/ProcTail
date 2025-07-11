# Syntax test script
param(
    [string]$ScriptPath = "$PSScriptRoot\integration-test.ps1"
)

Write-Host "Testing PowerShell script syntax..." -ForegroundColor Yellow
Write-Host "Script path: $ScriptPath" -ForegroundColor Gray

try {
    # Test if file exists
    if (-not (Test-Path $ScriptPath)) {
        Write-Host "ERROR: Script not found at: $ScriptPath" -ForegroundColor Red
        exit 1
    }
    
    # Parse the script
    $errors = $null
    $tokens = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        $ScriptPath,
        [ref]$tokens,
        [ref]$errors
    )
    
    if ($errors.Count -gt 0) {
        Write-Host "SYNTAX ERRORS FOUND:" -ForegroundColor Red
        foreach ($err in $errors) {
            Write-Host "  Line $($err.Extent.StartLineNumber): $($err.Message)" -ForegroundColor Red
            Write-Host "  Near: $($err.Extent.Text)" -ForegroundColor Yellow
        }
        exit 1
    } else {
        Write-Host "✓ No syntax errors found!" -ForegroundColor Green
    }
    
    # Try to load the script
    # Write-Host "Attempting to load script..." -ForegroundColor Gray
    # & $ScriptPath -SkipCleanup -KeepProcesses  # これは実行用
    # Write-Host "✓ Script syntax is valid!" -ForegroundColor Green
}
catch {
    Write-Host "ERROR loading script:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host "" -ForegroundColor Red
    Write-Host "Location:" -ForegroundColor Red
    Write-Host $_.InvocationInfo.PositionMessage -ForegroundColor Red
    exit 1
}