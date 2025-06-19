# Check event filtering configuration
# イベントフィルタリング設定の確認

Write-Host "Event Filtering Configuration Check" -ForegroundColor Yellow
Write-Host "====================================" -ForegroundColor Yellow

$configPath = "C:\Temp\ProcTailTest\host\appsettings.json"

if (-not (Test-Path $configPath)) {
    Write-Host "Configuration file not found: $configPath" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Read configuration
Write-Host "`nReading configuration..." -ForegroundColor Cyan
$config = Get-Content $configPath -Raw | ConvertFrom-Json

# Display ETW filtering settings
Write-Host "`nETW Filtering Configuration:" -ForegroundColor Yellow
Write-Host "MinimumProcessId: $($config.ETW.Filtering.MinimumProcessId)" -ForegroundColor Gray
Write-Host "ExcludeSystemProcesses: $($config.ETW.Filtering.ExcludeSystemProcesses)" -ForegroundColor Gray

Write-Host "`nExcluded Process Names:" -ForegroundColor Yellow
foreach ($name in $config.ETW.Filtering.ExcludedProcessNames) {
    Write-Host "  $name" -ForegroundColor Gray
}

Write-Host "`nIncluded File Extensions:" -ForegroundColor Yellow
foreach ($ext in $config.ETW.Filtering.IncludeFileExtensions) {
    Write-Host "  $ext" -ForegroundColor Gray
}

Write-Host "`nExcluded File Patterns:" -ForegroundColor Yellow
foreach ($pattern in $config.ETW.Filtering.ExcludeFilePatterns) {
    Write-Host "  $pattern" -ForegroundColor Gray
}

# Test notepad PID
$notepadPid = 66160
Write-Host "`nTesting Notepad PID: $notepadPid" -ForegroundColor Cyan

if ($notepadPid -lt $config.ETW.Filtering.MinimumProcessId) {
    Write-Host "❌ Notepad PID ($notepadPid) is below MinimumProcessId ($($config.ETW.Filtering.MinimumProcessId))" -ForegroundColor Red
} else {
    Write-Host "✅ Notepad PID ($notepadPid) passes MinimumProcessId filter" -ForegroundColor Green
}

# Check if notepad.exe is in excluded processes
$isExcluded = $config.ETW.Filtering.ExcludedProcessNames -contains "notepad.exe"
if ($isExcluded) {
    Write-Host "❌ notepad.exe is in excluded process list" -ForegroundColor Red
} else {
    Write-Host "✅ notepad.exe is not in excluded process list" -ForegroundColor Green
}

# Test file path filtering
$testFile = "C:\Users\ryoha\AppData\Local\Temp\proctail-test.txt"
Write-Host "`nTesting file path: $testFile" -ForegroundColor Cyan

$matchesExclude = $false
foreach ($pattern in $config.ETW.Filtering.ExcludeFilePatterns) {
    $regex = $pattern -replace '\*', '.*'
    if ($testFile -match $regex) {
        Write-Host "❌ File matches exclude pattern: $pattern" -ForegroundColor Red
        $matchesExclude = $true
        break
    }
}

if (-not $matchesExclude) {
    Write-Host "✅ File does not match any exclude patterns" -ForegroundColor Green
}

# Check .txt extension
$hasValidExtension = $config.ETW.Filtering.IncludeFileExtensions -contains ".txt"
if ($hasValidExtension) {
    Write-Host "✅ .txt extension is in include list" -ForegroundColor Green
} else {
    Write-Host "❌ .txt extension is not in include list" -ForegroundColor Red
}

# Recommendation
Write-Host "`nRecommendations:" -ForegroundColor Yellow
if ($notepadPid -lt $config.ETW.Filtering.MinimumProcessId) {
    Write-Host "- Lower MinimumProcessId to allow notepad events" -ForegroundColor Cyan
}
if ($isExcluded) {
    Write-Host "- Remove 'notepad.exe' from ExcludedProcessNames" -ForegroundColor Cyan
}
if ($matchesExclude) {
    Write-Host "- Review ExcludeFilePatterns to allow temp files" -ForegroundColor Cyan
}
if (-not $hasValidExtension) {
    Write-Host "- Add '.txt' to IncludeFileExtensions" -ForegroundColor Cyan
}

Read-Host "`nPress Enter to exit"