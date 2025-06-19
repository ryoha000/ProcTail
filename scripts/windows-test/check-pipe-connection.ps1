# Check Named Pipe connection
# Named Pipe接続を確認

Write-Host "Named Pipe Connection Check" -ForegroundColor Yellow
Write-Host "===========================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

# Local paths
$localRoot = "C:\Temp\ProcTailTest"
$localHostPath = Join-Path $localRoot "host\ProcTail.Host.exe"
$localCliPath = Join-Path $localRoot "cli\proctail.exe"

# Step 1: Check if Host is running
Write-Host "`nStep 1: Checking for running Host process..." -ForegroundColor Cyan
$hostProcesses = Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue
if ($hostProcesses) {
    foreach ($proc in $hostProcesses) {
        Write-Host "Found Host process: PID=$($proc.Id), Path=$($proc.Path)" -ForegroundColor Green
    }
} else {
    Write-Host "No Host process found" -ForegroundColor Yellow
}

# Step 2: Check Named Pipes
Write-Host "`nStep 2: Checking Named Pipes..." -ForegroundColor Cyan
try {
    $pipes = [System.IO.Directory]::GetFiles("\\.\\pipe\\") | Where-Object { $_ -like "*ProcTail*" }
    if ($pipes) {
        Write-Host "Found Named Pipes:" -ForegroundColor Green
        foreach ($pipe in $pipes) {
            Write-Host "  $pipe" -ForegroundColor Gray
        }
    } else {
        Write-Host "No ProcTail Named Pipes found" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "Error accessing Named Pipes: $_" -ForegroundColor Red
}

# Step 3: Start Host if not running
if (-not $hostProcesses) {
    Write-Host "`nStep 3: Starting Host..." -ForegroundColor Cyan
    
    if (Test-Path $localHostPath) {
        try {
            $hostProc = Start-Process -FilePath $localHostPath -PassThru -WindowStyle Normal
            Write-Host "Started Host with PID: $($hostProc.Id)" -ForegroundColor Green
            
            # Wait for initialization
            Write-Host "Waiting for Host to initialize..." -ForegroundColor Gray
            Start-Sleep -Seconds 5
            
            # Check if still running
            if (Get-Process -Id $hostProc.Id -ErrorAction SilentlyContinue) {
                Write-Host "Host is running" -ForegroundColor Green
            } else {
                Write-Host "Host exited" -ForegroundColor Red
            }
        }
        catch {
            Write-Host "Failed to start Host: $_" -ForegroundColor Red
        }
    } else {
        Write-Host "Host executable not found at: $localHostPath" -ForegroundColor Red
    }
}

# Step 4: Check Named Pipes again
Write-Host "`nStep 4: Re-checking Named Pipes after Host start..." -ForegroundColor Cyan
Start-Sleep -Seconds 2
try {
    $pipes = [System.IO.Directory]::GetFiles("\\.\\pipe\\") | Where-Object { $_ -like "*ProcTail*" }
    if ($pipes) {
        Write-Host "Found Named Pipes:" -ForegroundColor Green
        foreach ($pipe in $pipes) {
            Write-Host "  $pipe" -ForegroundColor Gray
        }
    } else {
        Write-Host "No ProcTail Named Pipes found" -ForegroundColor Red
        Write-Host "Host may not be creating the Named Pipe server" -ForegroundColor Red
    }
}
catch {
    Write-Host "Error accessing Named Pipes: $_" -ForegroundColor Red
}

# Step 5: Test CLI connection
Write-Host "`nStep 5: Testing CLI connection..." -ForegroundColor Cyan
if (Test-Path $localCliPath) {
    Write-Host "Trying 'proctail status' command..." -ForegroundColor Gray
    $statusResult = & $localCliPath status 2>&1
    Write-Host "Exit code: $LASTEXITCODE" -ForegroundColor Gray
    Write-Host "Result: $statusResult" -ForegroundColor Gray
}

# Step 6: Check Host logs
Write-Host "`nStep 6: Checking Host logs..." -ForegroundColor Cyan
$logFiles = @(
    "C:\ProcTail-Test-Logs\host-*.log",
    "C:\Temp\ProcTailTest\host\Logs\*.log"
)

foreach ($pattern in $logFiles) {
    $logs = Get-ChildItem $pattern -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($logs) {
        Write-Host "Found log: $($logs.FullName)" -ForegroundColor Green
        Write-Host "Last 20 lines:" -ForegroundColor Gray
        Get-Content $logs.FullName -Tail 20 | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    }
}

# Step 7: Check Windows Event Log
Write-Host "`nStep 7: Checking Windows Event Log..." -ForegroundColor Cyan
try {
    $events = Get-WinEvent -FilterHashtable @{
        LogName='Application'
        StartTime=(Get-Date).AddMinutes(-10)
    } | Where-Object { $_.Message -like "*ProcTail*" -or $_.ProviderName -like "*ProcTail*" } | Select-Object -First 5
    
    if ($events) {
        Write-Host "Found ProcTail-related events:" -ForegroundColor Yellow
        foreach ($event in $events) {
            Write-Host "Time: $($event.TimeCreated)" -ForegroundColor Gray
            Write-Host "Level: $($event.LevelDisplayName)" -ForegroundColor Gray
            Write-Host "Message: $($event.Message)" -ForegroundColor Gray
            Write-Host "---" -ForegroundColor Gray
        }
    } else {
        Write-Host "No ProcTail-related events found" -ForegroundColor Gray
    }
}
catch {
    Write-Host "Could not read event log: $_" -ForegroundColor Gray
}

Read-Host "`nPress Enter to exit"