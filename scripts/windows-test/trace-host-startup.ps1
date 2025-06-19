# Trace Host startup with detailed logging
# Host起動の詳細トレース

Write-Host "Host Startup Trace" -ForegroundColor Yellow
Write-Host "==================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Set environment variables for detailed .NET logging
$env:COREHOST_TRACE = "1"
$env:COREHOST_TRACE_VERBOSITY = "4"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

# Get paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$hostPath = Join-Path $projectRoot "publish\host\ProcTail.Host.exe"
$hostDir = Split-Path -Parent $hostPath

Write-Host "Host directory: $hostDir" -ForegroundColor Cyan

# Check for required files
Write-Host "`nChecking for required files..." -ForegroundColor Yellow
$requiredFiles = @(
    "ProcTail.Host.exe",
    "ProcTail.Host.dll",
    "ProcTail.Host.deps.json",
    "ProcTail.Host.runtimeconfig.json",
    "appsettings.json"
)

foreach ($file in $requiredFiles) {
    $filePath = Join-Path $hostDir $file
    if (Test-Path $filePath) {
        $fileInfo = Get-Item $filePath
        Write-Host "✓ $file ($('{0:N0}' -f $fileInfo.Length) bytes)" -ForegroundColor Green
    } else {
        Write-Host "✗ $file - NOT FOUND!" -ForegroundColor Red
    }
}

# Check for .NET runtime files
Write-Host "`nChecking for .NET runtime files..." -ForegroundColor Yellow
$runtimeFiles = @("hostfxr.dll", "hostpolicy.dll", "coreclr.dll")
foreach ($file in $runtimeFiles) {
    $filePath = Join-Path $hostDir $file
    if (Test-Path $filePath) {
        Write-Host "✓ $file found" -ForegroundColor Green
    } else {
        Write-Host "✗ $file - NOT FOUND!" -ForegroundColor Red
    }
}

# Read runtimeconfig.json
$runtimeConfigPath = Join-Path $hostDir "ProcTail.Host.runtimeconfig.json"
if (Test-Path $runtimeConfigPath) {
    Write-Host "`nRuntime config content:" -ForegroundColor Yellow
    Get-Content $runtimeConfigPath | Write-Host -ForegroundColor Gray
}

# Try to run with detailed tracing
Write-Host "`nRunning Host with .NET tracing enabled..." -ForegroundColor Yellow
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$traceLog = "C:\ProcTail-Test-Logs\host-trace-$timestamp.log"

try {
    Push-Location $hostDir
    Write-Host "Working directory: $(Get-Location)" -ForegroundColor Gray
    
    # Run and capture all output
    & $hostPath 2>&1 | Tee-Object -FilePath $traceLog
    
    Write-Host "`nHost exit code: $LASTEXITCODE" -ForegroundColor Cyan
}
catch {
    Write-Host "Error running Host: $_" -ForegroundColor Red
}
finally {
    Pop-Location
}

Write-Host "`nTrace log saved to: $traceLog" -ForegroundColor Cyan

# Try running with dotnet.exe directly
Write-Host "`nTrying to run with dotnet.exe..." -ForegroundColor Yellow
try {
    Push-Location $hostDir
    & dotnet ProcTail.Host.dll 2>&1 | Select-Object -First 20
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
}
finally {
    Pop-Location
}

Read-Host "`nPress Enter to exit"