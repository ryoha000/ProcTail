# PowerShell test script to verify basic functionality
param()

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   PowerShell Test Script" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

# 管理者権限チェック
try {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    
    if ($isAdmin) {
        Write-Host "✅ Running as Administrator" -ForegroundColor Green
    } else {
        Write-Host "❌ NOT running as Administrator" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ Error checking admin privileges: $($_.Exception.Message)" -ForegroundColor Red
}

# ファイル存在確認
$testRoot = 'C:\Temp\ProcTailTest'
$hostPath = "$testRoot\host\ProcTail.Host.exe"
$cliPath = "$testRoot\cli\proctail.exe"

Write-Host ""
Write-Host "File existence check:" -ForegroundColor Cyan
Write-Host "Test root: $testRoot" -ForegroundColor Gray

if (Test-Path $testRoot) {
    Write-Host "✅ Test directory exists" -ForegroundColor Green
} else {
    Write-Host "❌ Test directory missing" -ForegroundColor Red
}

if (Test-Path $hostPath) {
    Write-Host "✅ Host executable found" -ForegroundColor Green
} else {
    Write-Host "❌ Host executable missing: $hostPath" -ForegroundColor Red
}

if (Test-Path $cliPath) {
    Write-Host "✅ CLI executable found" -ForegroundColor Green
} else {
    Write-Host "❌ CLI executable missing: $cliPath" -ForegroundColor Red
}

# 実行ポリシー確認
Write-Host ""
Write-Host "Execution Policy:" -ForegroundColor Cyan
$policy = Get-ExecutionPolicy
Write-Host "Current policy: $policy" -ForegroundColor Gray

# システム情報
Write-Host ""
Write-Host "System Information:" -ForegroundColor Cyan
Write-Host "PowerShell Version: $($PSVersionTable.PSVersion)" -ForegroundColor Gray
Write-Host "OS: $($env:OS)" -ForegroundColor Gray
Write-Host "Computer: $($env:COMPUTERNAME)" -ForegroundColor Gray
Write-Host "User: $($env:USERNAME)" -ForegroundColor Gray

Write-Host ""
Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "PowerShell test completed successfully!" -ForegroundColor Green
Write-Host "===============================================" -ForegroundColor Yellow
Write-Host ""
Read-Host "Press Enter to exit"