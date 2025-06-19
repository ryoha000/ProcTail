# Start ProcTail Host
# ProcTail Host を起動

param(
    [string]$HostPath = "..\..\publish\host\ProcTail.Host.exe"
)

Write-Host "Starting ProcTail Host..." -ForegroundColor Yellow

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "This script requires administrator privileges. Please run as administrator." -ForegroundColor Red
    exit 1
}

# Resolve full path to Host executable
$fullHostPath = Resolve-Path $HostPath -ErrorAction SilentlyContinue
if (-not $fullHostPath) {
    Write-Host "Could not find ProcTail Host at: $HostPath" -ForegroundColor Red
    Write-Host "Please ensure the application has been built and published." -ForegroundColor Red
    exit 1
}

Write-Host "Starting Host from: $fullHostPath" -ForegroundColor Cyan

# Start Host in background
try {
    $hostProcess = Start-Process -FilePath $fullHostPath -PassThru -WindowStyle Hidden
    Write-Host "ProcTail Host started with PID: $($hostProcess.Id)" -ForegroundColor Green
    
    # Wait a moment for host to initialize
    Start-Sleep -Seconds 3
    
    # Check if process is still running
    if (Get-Process -Id $hostProcess.Id -ErrorAction SilentlyContinue) {
        Write-Host "Host is running successfully!" -ForegroundColor Green
        return $hostProcess.Id
    } else {
        Write-Host "Host process exited unexpectedly" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "Failed to start Host: $_" -ForegroundColor Red
    exit 1
}