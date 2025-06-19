# Check Host error by running it directly and capturing output
# Hostを直接実行してエラー出力をキャプチャ

Write-Host "Checking Host Error" -ForegroundColor Yellow
Write-Host "==================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Get paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$hostPath = Join-Path $projectRoot "publish\host\ProcTail.Host.exe"

Write-Host "Host path: $hostPath" -ForegroundColor Cyan

if (-not (Test-Path $hostPath)) {
    Write-Host "ERROR: Host executable not found!" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Create log directory
$logDir = "C:\ProcTail-Test-Logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$outputFile = "$logDir\host-error-$timestamp.log"
$errorFile = "$logDir\host-error-stderr-$timestamp.log"

Write-Host "Running Host and capturing output..." -ForegroundColor Yellow
Write-Host "Output file: $outputFile" -ForegroundColor Gray
Write-Host "Error file: $errorFile" -ForegroundColor Gray

# Run Host with output redirection
$pinfo = New-Object System.Diagnostics.ProcessStartInfo
$pinfo.FileName = $hostPath
$pinfo.RedirectStandardError = $true
$pinfo.RedirectStandardOutput = $true
$pinfo.UseShellExecute = $false
$pinfo.CreateNoWindow = $false
$pinfo.WorkingDirectory = Split-Path -Parent $hostPath

$p = New-Object System.Diagnostics.Process
$p.StartInfo = $pinfo

Write-Host "`nStarting Host process..." -ForegroundColor Cyan
$p.Start() | Out-Null

# Give it a moment
Start-Sleep -Seconds 2

# Check if still running
if ($p.HasExited) {
    Write-Host "Host exited with code: $($p.ExitCode)" -ForegroundColor Red
    
    # Read output
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    
    # Save to files
    $stdout | Out-File -FilePath $outputFile -Encoding UTF8
    $stderr | Out-File -FilePath $errorFile -Encoding UTF8
    
    # Display output
    if ($stdout) {
        Write-Host "`nStandard Output:" -ForegroundColor Yellow
        Write-Host $stdout
    }
    
    if ($stderr) {
        Write-Host "`nStandard Error:" -ForegroundColor Yellow
        Write-Host $stderr -ForegroundColor Red
    }
    
    # Check Windows Event Log
    Write-Host "`nChecking Windows Event Log for .NET errors..." -ForegroundColor Yellow
    try {
        $events = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='.NET Runtime'; StartTime=(Get-Date).AddMinutes(-5)} -MaxEvents 5 -ErrorAction SilentlyContinue
        if ($events) {
            Write-Host "Recent .NET Runtime errors:" -ForegroundColor Yellow
            foreach ($event in $events) {
                Write-Host "Time: $($event.TimeCreated)" -ForegroundColor Gray
                Write-Host "Message: $($event.Message)" -ForegroundColor Gray
                Write-Host "---" -ForegroundColor Gray
            }
        }
    }
    catch {
        Write-Host "Could not read event log" -ForegroundColor Gray
    }
} else {
    Write-Host "Host is still running! PID: $($p.Id)" -ForegroundColor Green
    Write-Host "Stopping Host..." -ForegroundColor Cyan
    $p.Kill()
    $p.WaitForExit()
}

Write-Host "`nLog files saved to:" -ForegroundColor Cyan
Write-Host "  $outputFile" -ForegroundColor White
Write-Host "  $errorFile" -ForegroundColor White

Read-Host "`nPress Enter to exit"