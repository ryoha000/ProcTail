# Simple PowerShell test - minimal version
Write-Host "=== Simple PowerShell Test ===" -ForegroundColor Yellow

# Test 1: Basic variable assignment
Write-Host "Test 1: Variable assignment" -ForegroundColor Cyan
$testVar = "Hello World"
Write-Host "testVar = '$testVar'" -ForegroundColor Green

# Test 2: Path variables
Write-Host "Test 2: Path variables" -ForegroundColor Cyan
$path1 = "C:/Temp/ProcTailTest"
$path2 = "C:\Temp\ProcTailTest"
Write-Host "path1 (forward slash) = '$path1'" -ForegroundColor Green
Write-Host "path2 (back slash) = '$path2'" -ForegroundColor Green

# Test 3: File existence
Write-Host "Test 3: File existence check" -ForegroundColor Cyan
if (Test-Path "C:/Temp") {
    Write-Host "C:/Temp exists" -ForegroundColor Green
} else {
    Write-Host "C:/Temp does not exist" -ForegroundColor Red
}

if (Test-Path "C:/Temp/ProcTailTest") {
    Write-Host "C:/Temp/ProcTailTest exists" -ForegroundColor Green
} else {
    Write-Host "C:/Temp/ProcTailTest does not exist" -ForegroundColor Red
}

# Test 4: Admin check
Write-Host "Test 4: Admin check" -ForegroundColor Cyan
try {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    $isAdmin = $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    Write-Host "Is Administrator: $isAdmin" -ForegroundColor Green
} catch {
    Write-Host "Admin check failed: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "=== Test Complete ===" -ForegroundColor Yellow
Read-Host "Press Enter to exit"