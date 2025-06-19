# Start Host for debugging
# デバッグ用Host起動

Write-Host "Starting Host for Debug" -ForegroundColor Yellow
Write-Host "======================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This script requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

$localHostPath = "C:\Temp\ProcTailTest\host\ProcTail.Host.exe"

# Check if Host is already running
$existingHost = Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue
if ($existingHost) {
    Write-Host "Found existing Host processes:" -ForegroundColor Yellow
    foreach ($proc in $existingHost) {
        Write-Host "  PID: $($proc.Id)" -ForegroundColor Gray
    }
    
    $response = Read-Host "Kill existing processes and start new one? (y/n)"
    if ($response -eq 'y') {
        $existingHost | Stop-Process -Force
        Start-Sleep -Seconds 3
        Write-Host "Existing processes killed" -ForegroundColor Green
    } else {
        Write-Host "Using existing Host process" -ForegroundColor Cyan
        Read-Host "Press Enter to exit"
        exit 0
    }
}

# Check if local copy exists
if (-not (Test-Path $localHostPath)) {
    Write-Host "Local Host copy not found. Creating local copy..." -ForegroundColor Yellow
    
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
    $sourceHostDir = Join-Path $projectRoot "publish\host"
    $localHostDir = "C:\Temp\ProcTailTest\host"
    
    New-Item -ItemType Directory -Path $localHostDir -Force | Out-Null
    Copy-Item -Path "$sourceHostDir\*" -Destination $localHostDir -Recurse -Force
    
    # Fix appsettings.json
    $configPath = Join-Path $localHostDir "appsettings.json"
    if (Test-Path $configPath) {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        $config.NamedPipe.PipeName = "ProcTail"
        $config | ConvertTo-Json -Depth 10 | Set-Content $configPath
        Write-Host "Configuration updated" -ForegroundColor Green
    }
    
    Write-Host "Local copy created" -ForegroundColor Green
}

# Start Host
Write-Host "`nStarting Host..." -ForegroundColor Yellow
Write-Host "Path: $localHostPath" -ForegroundColor Gray

try {
    $hostProcess = Start-Process -FilePath $localHostPath -PassThru -WorkingDirectory (Split-Path -Parent $localHostPath)
    Write-Host "Host started with PID: $($hostProcess.Id)" -ForegroundColor Green
    
    # Wait for initialization
    Write-Host "Waiting for Host initialization..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    
    # Check if still running
    if (Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue) {
        Write-Host "✓ Host is running successfully" -ForegroundColor Green
        
        # Test connection
        $localCliPath = "C:\Temp\ProcTailTest\cli\proctail.exe"
        if (Test-Path $localCliPath) {
            Write-Host "`nTesting connection..." -ForegroundColor Cyan
            $testResult = & $localCliPath status 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-Host "✓ Connection successful!" -ForegroundColor Green
                Write-Host "Status: $testResult" -ForegroundColor Gray
            } else {
                Write-Host "⚠ Connection failed: $testResult" -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host "✗ Host exited unexpectedly" -ForegroundColor Red
    }
}
catch {
    Write-Host "✗ Failed to start Host: $_" -ForegroundColor Red
}

Read-Host "Press Enter to exit"