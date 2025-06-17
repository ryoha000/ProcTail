#!/bin/bash
# Build and deploy script for ProcTail on Windows
# This script builds the project and copies binaries to the Windows environment

set -e

# Build configuration
BUILD_CONFIG="Debug"
TARGET_FRAMEWORK="net8.0"
WINDOWS_PATH="/mnt/f/workspace/tmp/proctail"

echo "=== ProcTail Build and Deploy Script ==="
echo "Build Config: $BUILD_CONFIG"
echo "Target Framework: $TARGET_FRAMEWORK"
echo "Windows Path: $WINDOWS_PATH"
echo

# Clean previous builds
echo "Cleaning previous builds..."
dotnet clean --configuration $BUILD_CONFIG

# Restore packages
echo "Restoring NuGet packages..."
dotnet restore

# Build the solution
echo "Building solution..."
dotnet build --configuration $BUILD_CONFIG --no-restore

# Test the build
echo "Running tests..."
dotnet test --configuration $BUILD_CONFIG --no-build --verbosity normal

# Create deployment directories
echo "Creating deployment directories..."
mkdir -p "$WINDOWS_PATH/host"
mkdir -p "$WINDOWS_PATH/cli"

# Copy Host service files
echo "Copying Host service files..."
cp -r "src/ProcTail.Host/bin/$BUILD_CONFIG/$TARGET_FRAMEWORK/"* "$WINDOWS_PATH/host/"

# Copy CLI files
echo "Copying CLI files..."
cp -r "src/ProcTail.Cli/bin/$BUILD_CONFIG/$TARGET_FRAMEWORK/"* "$WINDOWS_PATH/cli/"

# Copy configuration files
echo "Copying configuration files..."
cp "src/ProcTail.Host/appsettings.json" "$WINDOWS_PATH/host/"

# Make PowerShell scripts executable
echo "Creating PowerShell deployment scripts..."

# Create host restart script
cat > "$WINDOWS_PATH/restart-host.ps1" << 'EOF'
# PowerShell script to restart ProcTail Host service
param(
    [switch]$Force = $false
)

Write-Host "=== ProcTail Host Service Restart Script ===" -ForegroundColor Green

# Stop existing service if running
$existingProcess = Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue
if ($existingProcess) {
    Write-Host "Stopping existing ProcTail.Host process (PID: $($existingProcess.Id))" -ForegroundColor Yellow
    Stop-Process -Id $existingProcess.Id -Force
    Start-Sleep -Seconds 2
}

# Start new service
Write-Host "Starting ProcTail.Host service..." -ForegroundColor Green
$hostPath = Join-Path $PSScriptRoot "host\ProcTail.Host.exe"

if (-not (Test-Path $hostPath)) {
    Write-Error "ProcTail.Host.exe not found at: $hostPath"
    exit 1
}

# Start as background process
Start-Process -FilePath $hostPath -WorkingDirectory (Join-Path $PSScriptRoot "host") -WindowStyle Hidden

Write-Host "ProcTail.Host service started successfully!" -ForegroundColor Green
Write-Host "Check logs in: $(Join-Path $PSScriptRoot "host\Logs")" -ForegroundColor Cyan
EOF

# Create test script
cat > "$WINDOWS_PATH/test-etw-capture.ps1" << 'EOF'
# PowerShell script to test ETW event capture
param(
    [string]$TestFile = "test-file.txt"
)

Write-Host "=== ETW Event Capture Test Script ===" -ForegroundColor Green

$cliPath = Join-Path $PSScriptRoot "cli\ProcTail.Cli.exe"
$testFilePath = Join-Path $PSScriptRoot $TestFile

if (-not (Test-Path $cliPath)) {
    Write-Error "ProcTail.Cli.exe not found at: $cliPath"
    exit 1
}

Write-Host "1. Adding notepad.exe to watch targets..." -ForegroundColor Yellow
& $cliPath add --name notepad.exe --tag test-notepad

Write-Host "2. Listing current watch targets..." -ForegroundColor Yellow
& $cliPath list --format table

Write-Host "3. Starting notepad.exe..." -ForegroundColor Yellow
$notepadProcess = Start-Process -FilePath "notepad.exe" -ArgumentList $testFilePath -PassThru

Write-Host "4. Waiting for notepad to start..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

Write-Host "5. Getting events (should show process start)..." -ForegroundColor Yellow
& $cliPath events --tag test-notepad --format table

Write-Host "6. Instructions:" -ForegroundColor Cyan
Write-Host "   - Notepad is now open with file: $testFilePath" -ForegroundColor Cyan
Write-Host "   - Type some text and save the file (Ctrl+S)" -ForegroundColor Cyan
Write-Host "   - Then run: .\cli\ProcTail.Cli.exe events --tag test-notepad --format table" -ForegroundColor Cyan
Write-Host "   - Check logs: .\host\Logs\proctail-*.log" -ForegroundColor Cyan

Write-Host "7. Notepad Process ID: $($notepadProcess.Id)" -ForegroundColor Green
EOF

# Create log viewer script
cat > "$WINDOWS_PATH/view-logs.ps1" << 'EOF'
# PowerShell script to view ProcTail logs
param(
    [switch]$Follow = $false,
    [string]$Level = "Trace"
)

Write-Host "=== ProcTail Log Viewer ===" -ForegroundColor Green

$logDir = Join-Path $PSScriptRoot "host\Logs"
if (-not (Test-Path $logDir)) {
    Write-Error "Log directory not found: $logDir"
    exit 1
}

$latestLog = Get-ChildItem -Path $logDir -Filter "proctail-*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $latestLog) {
    Write-Error "No log files found in: $logDir"
    exit 1
}

Write-Host "Latest log file: $($latestLog.FullName)" -ForegroundColor Cyan

if ($Follow) {
    Write-Host "Following log file (Ctrl+C to stop)..." -ForegroundColor Yellow
    Get-Content -Path $latestLog.FullName -Wait -Tail 20
} else {
    Write-Host "Showing recent log entries..." -ForegroundColor Yellow
    Get-Content -Path $latestLog.FullName -Tail 50 | Where-Object { $_ -match $Level }
}
EOF

echo "Deployment completed successfully!"
echo
echo "To test on Windows:"
echo "1. Open PowerShell as Administrator in: $WINDOWS_PATH"
echo "2. Run: .\restart-host.ps1"
echo "3. Run: .\test-etw-capture.ps1"
echo "4. Run: .\view-logs.ps1 -Follow"
echo
echo "Files deployed to:"
echo "- Host service: $WINDOWS_PATH/host/"
echo "- CLI tool: $WINDOWS_PATH/cli/"
echo "- PowerShell scripts: $WINDOWS_PATH/*.ps1"