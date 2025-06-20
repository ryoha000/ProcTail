# Host Startup Diagnostic Script
# このスクリプトはHostプロセスの起動問題を詳細に診断します

param(
    [switch]$Verbose
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   ProcTail Host Startup Diagnostic" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

# Admin check
try {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "ERROR: This diagnostic requires administrator privileges." -ForegroundColor Red
        Read-Host "Press Enter to exit"
        exit 1
    }
} catch {
    Write-Host "ERROR checking administrator privileges: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "Administrator privileges confirmed" -ForegroundColor Green

# File paths
$testRoot = "C:/Temp/ProcTailTest"
$hostDir = "C:/Temp/ProcTailTest/host"
$hostPath = "C:/Temp/ProcTailTest/host/win-x64/ProcTail.Host.exe"
$configPath = "C:/Temp/ProcTailTest/host/appsettings.json"

Write-Host ""
Write-Host "=== File System Check ===" -ForegroundColor Cyan

# Check if files exist
$checks = @(
    @{Path = $testRoot; Name = "Test Root Directory"},
    @{Path = $hostDir; Name = "Host Directory"},
    @{Path = $hostPath; Name = "Host Executable"},
    @{Path = $configPath; Name = "Configuration File"}
)

foreach ($check in $checks) {
    if (Test-Path $check.Path) {
        Write-Host "✅ $($check.Name): Found" -ForegroundColor Green
        if ($Verbose -and (Test-Path $check.Path -PathType Leaf)) {
            $item = Get-Item $check.Path
            Write-Host "   Size: $($item.Length) bytes, Modified: $($item.LastWriteTime)" -ForegroundColor Gray
        }
    } else {
        Write-Host "❌ $($check.Name): Missing - $($check.Path)" -ForegroundColor Red
    }
}

# Check configuration file content
Write-Host ""
Write-Host "=== Configuration Check ===" -ForegroundColor Cyan
if (Test-Path $configPath) {
    try {
        $config = Get-Content $configPath -Raw | ConvertFrom-Json
        Write-Host "✅ Configuration file is valid JSON" -ForegroundColor Green
        
        if ($config.NamedPipe) {
            Write-Host "✅ NamedPipe configuration found" -ForegroundColor Green
            Write-Host "   PipeName: $($config.NamedPipe.PipeName)" -ForegroundColor Gray
        } else {
            Write-Host "⚠️  NamedPipe configuration missing" -ForegroundColor Yellow
        }
        
        if ($config.Serilog) {
            Write-Host "✅ Serilog configuration found" -ForegroundColor Green
        } else {
            Write-Host "⚠️  Serilog configuration missing" -ForegroundColor Yellow
        }
        
        if ($Verbose) {
            Write-Host "--- Configuration Content ---" -ForegroundColor Gray
            Write-Host $config -ForegroundColor Gray
        }
    } catch {
        Write-Host "❌ Configuration file is invalid: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "❌ Configuration file not found" -ForegroundColor Red
}

# Check .NET runtime
Write-Host ""
Write-Host "=== .NET Runtime Check ===" -ForegroundColor Cyan
try {
    $dotnetInfo = dotnet --info 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "✅ .NET CLI is available" -ForegroundColor Green
        $runtimeLines = $dotnetInfo | Where-Object { $_ -like "*Microsoft.NETCore.App*" }
        if ($runtimeLines) {
            Write-Host "✅ .NET Runtime versions found:" -ForegroundColor Green
            foreach ($line in $runtimeLines) {
                Write-Host "   $line" -ForegroundColor Gray
            }
        } else {
            Write-Host "⚠️  No .NET Runtime versions found" -ForegroundColor Yellow
        }
    } else {
        Write-Host "❌ .NET CLI not available or error occurred" -ForegroundColor Red
    }
} catch {
    Write-Host "❌ Error checking .NET runtime: $($_.Exception.Message)" -ForegroundColor Red
}

# Check dependencies
Write-Host ""
Write-Host "=== Dependency Check ===" -ForegroundColor Cyan
$hostDirPath = $hostDir + "/win-x64"
if (Test-Path $hostDirPath) {
    $dllFiles = Get-ChildItem -Path $hostDirPath -Filter "*.dll" | Select-Object -First 10
    Write-Host "✅ Host directory contains $($dllFiles.Count) DLL files (showing first 10):" -ForegroundColor Green
    foreach ($dll in $dllFiles) {
        Write-Host "   $($dll.Name)" -ForegroundColor Gray
    }
    
    # Check for specific important DLLs
    $importantDlls = @(
        "Microsoft.Diagnostics.Tracing.TraceEvent.dll",
        "System.IO.Pipes.dll",
        "Serilog.dll"
    )
    
    foreach ($dll in $importantDlls) {
        $dllPath = Join-Path $hostDirPath $dll
        if (Test-Path $dllPath) {
            Write-Host "✅ Found: $dll" -ForegroundColor Green
        } else {
            Write-Host "❌ Missing: $dll" -ForegroundColor Red
        }
    }
} else {
    Write-Host "❌ Host win-x64 directory not found" -ForegroundColor Red
}

# Clean up any existing processes
Write-Host ""
Write-Host "=== Process Cleanup ===" -ForegroundColor Cyan
$existingProcesses = Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue
if ($existingProcesses) {
    Write-Host "⚠️  Found $($existingProcesses.Count) existing Host processes, stopping them..." -ForegroundColor Yellow
    $existingProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Write-Host "✅ Existing processes stopped" -ForegroundColor Green
} else {
    Write-Host "✅ No existing Host processes found" -ForegroundColor Green
}

# ETW Session cleanup
Write-Host ""
Write-Host "=== ETW Session Cleanup ===" -ForegroundColor Cyan
$etwSessions = @("ProcTail", "ProcTail-Dev", "NT Kernel Logger")
foreach ($session in $etwSessions) {
    try {
        $result = logman stop $session -ets 2>$null
        if ($LASTEXITCODE -eq 0) {
            Write-Host "✅ Stopped ETW session: $session" -ForegroundColor Green
        }
    } catch {
        # Session not running, ignore
    }
}

# Direct Host execution test
Write-Host ""
Write-Host "=== Direct Host Execution Test ===" -ForegroundColor Cyan
Write-Host "Attempting to start Host directly..." -ForegroundColor Gray

if (Test-Path $hostPath) {
    try {
        # Create a log directory for Host output
        $logDir = "C:/ProcTail-Test-Logs"
        if (-not (Test-Path $logDir)) {
            New-Item -ItemType Directory -Path $logDir -Force | Out-Null
        }
        
        $outputFile = "$logDir/host-startup-$(Get-Date -Format 'yyyyMMdd-HHmmss').log"
        
        Write-Host "Starting Host with output logging to: $outputFile" -ForegroundColor Gray
        Write-Host "Host will run for 10 seconds, then we'll check the output..." -ForegroundColor Gray
        
        # Start Host process with output redirection
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $hostPath
        $psi.WorkingDirectory = $hostDir
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $false  # Show window for debugging
        
        $process = [System.Diagnostics.Process]::Start($psi)
        Write-Host "Host started with PID: $($process.Id)" -ForegroundColor Green
        
        # Wait and check status
        $checkInterval = 1
        $maxWaitTime = 10
        $waited = 0
        
        while ($waited -lt $maxWaitTime -and -not $process.HasExited) {
            Start-Sleep -Seconds $checkInterval
            $waited += $checkInterval
            Write-Host "Host running... ($waited/$maxWaitTime seconds)" -ForegroundColor Gray
        }
        
        if ($process.HasExited) {
            Write-Host "❌ Host process exited after $waited seconds" -ForegroundColor Red
            Write-Host "Exit code: $($process.ExitCode)" -ForegroundColor Red
            
            # Read output
            $stdout = $process.StandardOutput.ReadToEnd()
            $stderr = $process.StandardError.ReadToEnd()
            
            if ($stdout) {
                Write-Host "--- Standard Output ---" -ForegroundColor Yellow
                Write-Host $stdout -ForegroundColor White
            }
            
            if ($stderr) {
                Write-Host "--- Standard Error ---" -ForegroundColor Red
                Write-Host $stderr -ForegroundColor White
            }
            
            # Save to log file
            $logContent = @"
Host Startup Diagnostic Log - $(Get-Date)
Exit Code: $($process.ExitCode)

=== Standard Output ===
$stdout

=== Standard Error ===
$stderr
"@
            Set-Content -Path $outputFile -Value $logContent
            Write-Host "Log saved to: $outputFile" -ForegroundColor Gray
            
        } else {
            Write-Host "✅ Host is running successfully!" -ForegroundColor Green
            Write-Host "Stopping Host for diagnostic completion..." -ForegroundColor Gray
            $process | Stop-Process -Force
        }
        
    } catch {
        Write-Host "❌ Error starting Host: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Exception Type: $($_.Exception.GetType().FullName)" -ForegroundColor Red
        if ($_.Exception.InnerException) {
            Write-Host "Inner Exception: $($_.Exception.InnerException.Message)" -ForegroundColor Red
        }
    }
} else {
    Write-Host "❌ Host executable not found for direct test" -ForegroundColor Red
}

# Summary
Write-Host ""
Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   Diagnostic Complete" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "Log files are saved in: C:/ProcTail-Test-Logs" -ForegroundColor Gray
Write-Host ""
Read-Host "Press Enter to exit"