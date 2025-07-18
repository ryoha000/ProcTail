# ProcTail Integration Test Log Analyzer - Working Version
param(
    [Parameter()]
    [string]$LogDirectory = "C:/Temp/ProcTailTest/logs",
    
    [Parameter()]
    [string]$EventsLogFile,
    
    [Parameter()]
    [switch]$ShowRawEvents,
    
    [Parameter()]
    [switch]$OutputJson
)

if (-not $OutputJson) {
    Write-Host "===============================================" -ForegroundColor Yellow
    Write-Host "   ProcTail Log Analyzer" -ForegroundColor Yellow
    Write-Host "===============================================" -ForegroundColor Yellow
    Write-Host ""
}

# Find log file
if ($EventsLogFile -and (Test-Path $EventsLogFile)) {
    $logFile = $EventsLogFile
} else {
    # Find latest log directory
    if (Test-Path $LogDirectory) {
        $latestLogDir = Get-ChildItem $LogDirectory -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($latestLogDir) {
            $eventsLogPath = Join-Path $latestLogDir.FullName "events.log"
            if (Test-Path $eventsLogPath) {
                $logFile = $eventsLogPath
                if (-not $OutputJson) {
                Write-Host "Using latest log: $($latestLogDir.Name)/events.log" -ForegroundColor Gray
            }
            }
        }
    }
}

if (-not $logFile -or -not (Test-Path $logFile)) {
    if ($OutputJson) {
        $result = @{
            Success = $false
            Error = "No event log file found"
        }
        Write-Output (ConvertTo-Json $result -Depth 5)
    } else {
        Write-Host "ERROR: No event log file found" -ForegroundColor Red
    }
    exit 1
}

if (-not $OutputJson) {
    Write-Host "Analyzing: $logFile" -ForegroundColor Cyan
    Write-Host ""
}

# Read and parse log file
$content = Get-Content $logFile -Encoding UTF8
$totalLines = $content.Count
$eventLines = @()

foreach ($line in $content) {
    if ($line -match '^\|\s*\d{4}-\d{2}-\d{2}') {
        $eventLines += $line
    }
}

if (-not $OutputJson) {
    Write-Host "Total lines in file: $totalLines"
    Write-Host "Event lines found: $($eventLines.Count)"
    Write-Host ""
}

# Analyze events
$eventTypes = @{}
$pidEvents = @{}
$fileOperations = @{}

foreach ($line in $eventLines) {
    # Extract event type from parentheses
    if ($line -match '\((FileIO/[^)]+)\)') {
        $eventType = $matches[1]
        if (-not $eventTypes.ContainsKey($eventType)) {
            $eventTypes[$eventType] = 0
        }
        $eventTypes[$eventType] += 1
    }
    elseif ($line -match '\((Process/[^)]+)\)') {
        $eventType = $matches[1]
        if (-not $eventTypes.ContainsKey($eventType)) {
            $eventTypes[$eventType] = 0
        }
        $eventTypes[$eventType] += 1
    }
    
    # Extract PID
    if ($line -match '\|\s*(\d+)\s*\|') {
        $processId = $matches[1]
        if (-not $pidEvents.ContainsKey($processId)) {
            $pidEvents[$processId] = 0
        }
        $pidEvents[$processId] += 1
    }
    
    # Extract file path
    if ($line -match '\|\s*([A-Za-z]:\\[^|]+)\s*\(') {
        $filePath = $matches[1].Trim()
        if (-not $fileOperations.ContainsKey($filePath)) {
            $fileOperations[$filePath] = 0
        }
        $fileOperations[$filePath] += 1
    }
}

# Prepare results
if ($OutputJson) {
    # Build JSON output
    $result = @{
        Success = $true
        TotalLines = $totalLines
        EventLines = $eventLines.Count
        EventTypes = @{}
        ProcessEvents = @{}
        FileOperations = @{}
        Analysis = @{
            HasFileWrite = $false
            HasFileCreate = $false
            HasFileDelete = $false
            HasProcessStart = $false
            HasProcessEnd = $false
            TestProcessFiles = @()
        }
    }
    
    # Convert hashtables to arrays for JSON serialization
    if ($eventTypes.Count -gt 0) {
        $sortedEventTypes = $eventTypes.GetEnumerator() | Sort-Object Value -Descending
        $totalEvents = ($eventTypes.Values | Measure-Object -Sum).Sum
        
        foreach ($eventType in $sortedEventTypes) {
            $percentage = [math]::Round(($eventType.Value / $totalEvents) * 100, 1)
            $result.EventTypes[$eventType.Key] = @{
                Count = $eventType.Value
                Percentage = $percentage
            }
            
            # Set analysis flags
            if ($eventType.Key -eq "FileIO/Write") { $result.Analysis.HasFileWrite = $true }
            if ($eventType.Key -eq "FileIO/Create") { $result.Analysis.HasFileCreate = $true }
            if ($eventType.Key -eq "FileIO/Delete") { $result.Analysis.HasFileDelete = $true }
            if ($eventType.Key -eq "Process/Start") { $result.Analysis.HasProcessStart = $true }
            if ($eventType.Key -eq "Process/End") { $result.Analysis.HasProcessEnd = $true }
        }
    }
    
    if ($pidEvents.Count -gt 0) {
        $sortedPids = $pidEvents.GetEnumerator() | Sort-Object Value -Descending
        $totalPidEvents = ($pidEvents.Values | Measure-Object -Sum).Sum
        
        foreach ($pidEntry in $sortedPids) {
            $percentage = [math]::Round(($pidEntry.Value / $totalPidEvents) * 100, 1)
            
            # Try to get process name
            $processName = ""
            try {
                $process = Get-Process -Id $pidEntry.Key -ErrorAction SilentlyContinue
                if ($process) {
                    $processName = $process.Name
                }
            } catch {
                # Process already exited
            }
            
            $result.ProcessEvents[$pidEntry.Key] = @{
                Count = $pidEntry.Value
                Percentage = $percentage
                ProcessName = $processName
            }
        }
    }
    
    if ($fileOperations.Count -gt 0) {
        $topFiles = $fileOperations.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 10
        
        foreach ($file in $topFiles) {
            $fileName = Split-Path $file.Key -Leaf
            $result.FileOperations[$file.Key] = @{
                FileName = $fileName
                Count = $file.Value
            }
            
            # Check for test process files
            if ($fileName -match "continuous_\d+" -or $file.Key -match "TestFiles") {
                $result.Analysis.TestProcessFiles += $file.Key
            }
        }
    }
    
    # Output JSON
    Write-Output (ConvertTo-Json $result -Depth 5)
    
} else {
    # Display results (original format)
    Write-Host "================= ANALYSIS RESULTS ==================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Event Types Summary:" -ForegroundColor Yellow
    Write-Host "-------------------" -ForegroundColor Gray
    
    if ($eventTypes.Count -gt 0) {
        $sortedEventTypes = $eventTypes.GetEnumerator() | Sort-Object Value -Descending
        $totalEvents = ($eventTypes.Values | Measure-Object -Sum).Sum
        
        foreach ($eventType in $sortedEventTypes) {
            $percentage = [math]::Round(($eventType.Value / $totalEvents) * 100, 1)
            Write-Host ("  {0,-20} : {1,5} events ({2,5:F1}%)" -f $eventType.Key, $eventType.Value, $percentage) -ForegroundColor White
        }
    } else {
        Write-Host "  No events found" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "Process ID Summary:" -ForegroundColor Yellow
    Write-Host "------------------" -ForegroundColor Gray
    
    if ($pidEvents.Count -gt 0) {
        $sortedPids = $pidEvents.GetEnumerator() | Sort-Object Value -Descending
        $totalPidEvents = ($pidEvents.Values | Measure-Object -Sum).Sum
        
        foreach ($pidEntry in $sortedPids) {
            $percentage = [math]::Round(($pidEntry.Value / $totalPidEvents) * 100, 1)
            
            # Try to get process name
            $processName = ""
            try {
                $process = Get-Process -Id $pidEntry.Key -ErrorAction SilentlyContinue
                if ($process) {
                    $processName = " ($($process.Name))"
                }
            } catch {
                # Process already exited
            }
            
            Write-Host ("  PID {0,-8}{1,-20} : {2,5} events ({3,5:F1}%)" -f $pidEntry.Key, $processName, $pidEntry.Value, $percentage) -ForegroundColor White
        }
    } else {
        Write-Host "  No process events found" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "Top 10 Files by Operation Count:" -ForegroundColor Yellow
    Write-Host "-------------------------------" -ForegroundColor Gray
    
    if ($fileOperations.Count -gt 0) {
        $topFiles = $fileOperations.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 10
        
        foreach ($file in $topFiles) {
            $fileName = Split-Path $file.Key -Leaf
            Write-Host ("  {0,-40} : {1,3} ops" -f $fileName, $file.Value) -ForegroundColor White
        }
    } else {
        Write-Host "  No file operations found" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "====================================================" -ForegroundColor Cyan
}

# Show raw events if requested (only in non-JSON mode)
if (-not $OutputJson) {
    if ($ShowRawEvents) {
        Write-Host ""
        Write-Host "Raw Events:" -ForegroundColor Yellow
        Write-Host "-----------" -ForegroundColor Gray
        foreach ($line in $eventLines) {
            Write-Host $line
        }
    }
    
    Write-Host ""
    Write-Host "Analysis complete." -ForegroundColor Green
}