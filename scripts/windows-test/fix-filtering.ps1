# Fix filtering configuration to allow Temp files
# Tempファイルを許可するようにフィルタリング設定を修正

Write-Host "Fixing ETW Filtering Configuration" -ForegroundColor Yellow
Write-Host "===================================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

$configPath = "C:\Temp\ProcTailTest\host\appsettings.json"

if (-not (Test-Path $configPath)) {
    Write-Host "Configuration file not found: $configPath" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Backup original config
$backupPath = "$configPath.backup"
Copy-Item $configPath $backupPath -Force
Write-Host "Configuration backed up to: $backupPath" -ForegroundColor Green

# Read and modify configuration
Write-Host "`nModifying configuration..." -ForegroundColor Cyan
$config = Get-Content $configPath -Raw | ConvertFrom-Json

# Display current exclude patterns
Write-Host "`nCurrent exclude patterns:" -ForegroundColor Yellow
foreach ($pattern in $config.ETW.Filtering.ExcludeFilePatterns) {
    Write-Host "  $pattern" -ForegroundColor Gray
}

# Remove *\Temp\* pattern
$originalPatterns = $config.ETW.Filtering.ExcludeFilePatterns
$newPatterns = @()
foreach ($pattern in $originalPatterns) {
    if ($pattern -ne "*\Temp\*") {
        $newPatterns += $pattern
    } else {
        Write-Host "Removing pattern: $pattern" -ForegroundColor Yellow
    }
}
$config.ETW.Filtering.ExcludeFilePatterns = $newPatterns

# Also lower MinimumProcessId to be safe
Write-Host "`nLowering MinimumProcessId from $($config.ETW.Filtering.MinimumProcessId) to 4..." -ForegroundColor Cyan
$config.ETW.Filtering.MinimumProcessId = 4

# Save modified configuration
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath
Write-Host "`nConfiguration updated!" -ForegroundColor Green

# Display new exclude patterns
Write-Host "`nNew exclude patterns:" -ForegroundColor Yellow
foreach ($pattern in $config.ETW.Filtering.ExcludeFilePatterns) {
    Write-Host "  $pattern" -ForegroundColor Gray
}

Write-Host "`n" -ForegroundColor Yellow
Write-Host "IMPORTANT: You need to restart the Host for changes to take effect!" -ForegroundColor Yellow
Write-Host "Run: .\scripts\windows-test\restart-host.ps1" -ForegroundColor Cyan

Read-Host "`nPress Enter to exit"