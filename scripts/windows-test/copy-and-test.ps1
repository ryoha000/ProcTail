# Copy to Windows local disk and test
# Windowsローカルディスクにコピーしてテスト

Write-Host "Copy and Test - Copy files to C:\Temp and run" -ForegroundColor Yellow
Write-Host "==============================================" -ForegroundColor Yellow

# Check admin rights
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "ERROR: This test requires administrator privileges." -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Administrator privileges confirmed" -ForegroundColor Green

# Source paths (WSL)
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
$sourceHostDir = Join-Path $projectRoot "publish\host"
$sourceCliDir = Join-Path $projectRoot "publish\cli"

# Target paths (Windows local)
$targetRoot = "C:\Temp\ProcTailTest"
$targetHostDir = Join-Path $targetRoot "host"
$targetCliDir = Join-Path $targetRoot "cli"

Write-Host "`nCreating target directories..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $targetRoot -Force | Out-Null
New-Item -ItemType Directory -Path $targetHostDir -Force | Out-Null
New-Item -ItemType Directory -Path $targetCliDir -Force | Out-Null

# Copy files
Write-Host "`nCopying Host files..." -ForegroundColor Cyan
try {
    Copy-Item -Path "$sourceHostDir\*" -Destination $targetHostDir -Recurse -Force
    Write-Host "✓ Host files copied to: $targetHostDir" -ForegroundColor Green
}
catch {
    Write-Host "✗ Failed to copy Host files: $_" -ForegroundColor Red
}

Write-Host "`nCopying CLI files..." -ForegroundColor Cyan
try {
    Copy-Item -Path "$sourceCliDir\*" -Destination $targetCliDir -Recurse -Force
    Write-Host "✓ CLI files copied to: $targetCliDir" -ForegroundColor Green
}
catch {
    Write-Host "✗ Failed to copy CLI files: $_" -ForegroundColor Red
}

# Test from local copy
$localHostPath = Join-Path $targetHostDir "ProcTail.Host.exe"
$localCliPath = Join-Path $targetCliDir "proctail.exe"

Write-Host "`nTesting from local copy..." -ForegroundColor Yellow

# Test Host
if (Test-Path $localHostPath) {
    Write-Host "`nStarting Host from local copy..." -ForegroundColor Cyan
    Write-Host "Path: $localHostPath" -ForegroundColor Gray
    
    try {
        Push-Location $targetHostDir
        Write-Host "Changed to directory: $targetHostDir" -ForegroundColor Gray
        Write-Host "`nRunning Host..." -ForegroundColor Yellow
        
        # Run Host and wait for output
        $proc = Start-Process -FilePath $localHostPath -PassThru -RedirectStandardOutput "$targetRoot\host-stdout.log" -RedirectStandardError "$targetRoot\host-stderr.log" -WindowStyle Normal
        
        Write-Host "Host started with PID: $($proc.Id)" -ForegroundColor Cyan
        
        # Wait a bit
        Start-Sleep -Seconds 5
        
        if (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue) {
            Write-Host "✓ Host is still running after 5 seconds!" -ForegroundColor Green
            Write-Host "Stopping Host..." -ForegroundColor Cyan
            Stop-Process -Id $proc.Id -Force
        } else {
            Write-Host "✗ Host exited" -ForegroundColor Red
            
            # Read output files
            if (Test-Path "$targetRoot\host-stdout.log") {
                Write-Host "`nSTDOUT:" -ForegroundColor Yellow
                Get-Content "$targetRoot\host-stdout.log"
            }
            
            if (Test-Path "$targetRoot\host-stderr.log") {
                Write-Host "`nSTDERR:" -ForegroundColor Yellow
                Get-Content "$targetRoot\host-stderr.log"
            }
        }
    }
    catch {
        Write-Host "Error running Host: $_" -ForegroundColor Red
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "✗ Local Host copy not found at: $localHostPath" -ForegroundColor Red
}

# Test CLI
if (Test-Path $localCliPath) {
    Write-Host "`nTesting CLI from local copy..." -ForegroundColor Cyan
    Write-Host "Path: $localCliPath" -ForegroundColor Gray
    
    try {
        $cliOutput = & $localCliPath --help 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✓ CLI help command works!" -ForegroundColor Green
        } else {
            Write-Host "✗ CLI help command failed" -ForegroundColor Red
            Write-Host "Output: $cliOutput" -ForegroundColor Gray
        }
    }
    catch {
        Write-Host "Error running CLI: $_" -ForegroundColor Red
    }
} else {
    Write-Host "✗ Local CLI copy not found at: $localCliPath" -ForegroundColor Red
}

Write-Host "`nTest completed. Files copied to: $targetRoot" -ForegroundColor Yellow
Read-Host "Press Enter to exit"