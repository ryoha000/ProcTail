# ProcTail Integration Test Log Analyzer
# integration-test.ps1の実行で生成されたログを解析するスクリプト

param(
    [Parameter()]
    [string]$LogDirectory = "C:/Temp/ProcTailTest/logs",
    
    [Parameter()]
    [string]$EventsLogFile,
    
    [Parameter()]
    [switch]$ShowRawEvents
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   ProcTail Log Analyzer" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

# ログディレクトリの検索
if (-not $EventsLogFile) {
    if (Test-Path $LogDirectory) {
        # 最新のログディレクトリを検索
        $latestLogDir = Get-ChildItem $LogDirectory -Directory | Sort-Object Name -Descending | Select-Object -First 1
        if ($latestLogDir) {
            $LogDirectory = $latestLogDir.FullName
            Write-Host "Using latest log directory: $LogDirectory" -ForegroundColor Gray
        }
    } else {
        Write-Host "ERROR: Log directory not found: $LogDirectory" -ForegroundColor Red
        exit 1
    }
}

# イベントログの解析
function Analyze-EventsLog {
    param(
        [string]$EventsContent
    )
    
    # イベントタイプの統計
    $eventTypes = @{}
    $pidEvents = @{}
    $totalEvents = 0
    
    # イベントログの各行を解析
    $lines = $EventsContent -split "`n"
    
    foreach ($line in $lines) {
        # イベント行のパターン: | 2024-01-01 12:00:00 | 1234 | FileIO/Write | ... |
        if ($line -match '^\s*\|\s*\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\s*\|\s*(\d+)\s*\|\s*([^|]+)\s*\|') {
            $pid = $matches[1]
            $eventType = $matches[2].Trim()
            
            $totalEvents++
            
            # イベントタイプ別カウント
            if ($eventTypes.ContainsKey($eventType)) {
                $eventTypes[$eventType]++
            } else {
                $eventTypes[$eventType] = 1
            }
            
            # PID別カウント
            if ($pidEvents.ContainsKey($pid)) {
                $pidEvents[$pid]++
            } else {
                $pidEvents[$pid] = 1
            }
        }
    }
    
    return @{
        TotalEvents = $totalEvents
        EventTypes = $eventTypes
        PidEvents = $pidEvents
    }
}

# integration-test.ps1の出力からイベント部分を抽出
function Extract-EventsFromTestOutput {
    param(
        [string]$OutputFile
    )
    
    if (-not (Test-Path $OutputFile)) {
        return $null
    }
    
    $content = Get-Content $OutputFile -Raw -Encoding UTF8
    
    # "================= EVENTS =================" セクションを探す
    if ($content -match '================= EVENTS =================\s*\n(.*?)\s*==========================================') {
        return $matches[1]
    }
    
    return $null
}

# メイン処理
Write-Host ""
Write-Host "Analyzing log files..." -ForegroundColor Cyan
Write-Host ""

$eventsContent = $null

# イベントログファイルが指定されている場合
if ($EventsLogFile -and (Test-Path $EventsLogFile)) {
    $eventsContent = Get-Content $EventsLogFile -Raw -Encoding UTF8
    Write-Host "Reading events from: $EventsLogFile" -ForegroundColor Gray
}
# ログディレクトリから検索する場合
else {
    # まず events.log を探す（integration-test.ps1が作成）
    $eventsLogPath = Join-Path $LogDirectory "events.log"
    if (Test-Path $eventsLogPath) {
        $eventsContent = Get-Content $eventsLogPath -Raw -Encoding UTF8
        Write-Host "Reading events from: events.log" -ForegroundColor Gray
    } else {
        # integration-test.ps1の出力ログを探す
        $testOutputFiles = @(
            "integration-test-output.log",
            "test-output.log",
            "console-output.log"
        )
        
        foreach ($file in $testOutputFiles) {
            $fullPath = Join-Path $LogDirectory $file
            if (Test-Path $fullPath) {
                Write-Host "Found test output file: $file" -ForegroundColor Gray
                $eventsContent = Extract-EventsFromTestOutput -OutputFile $fullPath
                if ($eventsContent) {
                    Write-Host "Extracted events from test output" -ForegroundColor Green
                    break
                }
            }
        }
        
        # イベントログファイルを直接探す
        if (-not $eventsContent) {
            $eventFiles = Get-ChildItem $LogDirectory -Filter "*events*.log" -ErrorAction SilentlyContinue
            if ($eventFiles) {
                $eventFile = $eventFiles | Select-Object -First 1
                $eventsContent = Get-Content $eventFile.FullName -Raw -Encoding UTF8
                Write-Host "Reading events from: $($eventFile.Name)" -ForegroundColor Gray
            }
        }
    }
}

# イベントが見つからない場合は、コンソール出力をシミュレート
if (-not $eventsContent) {
    Write-Host "No event log files found. Looking for captured console output..." -ForegroundColor Yellow
    
    # PowerShellコンソール出力をキャプチャする方法を探す
    $consoleCapture = Join-Path $LogDirectory "console-capture.txt"
    if (Test-Path $consoleCapture) {
        $eventsContent = Get-Content $consoleCapture -Raw -Encoding UTF8
        Write-Host "Found console capture file" -ForegroundColor Gray
    } else {
        Write-Host "ERROR: No event data found in log directory" -ForegroundColor Red
        Write-Host "Please ensure integration-test.ps1 was run and generated output" -ForegroundColor Yellow
        exit 1
    }
}

# イベントを解析
if ($eventsContent) {
    $analysis = Analyze-EventsLog -EventsContent $eventsContent
    
    # 結果を表示
    Write-Host "================= ANALYSIS RESULTS =================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Total Events Captured: $($analysis.TotalEvents)" -ForegroundColor White
    Write-Host ""
    
    # イベントタイプ別統計
    Write-Host "Event Types Summary:" -ForegroundColor Yellow
    Write-Host "-------------------" -ForegroundColor Gray
    
    if ($analysis.EventTypes.Count -gt 0) {
        $sortedEventTypes = $analysis.EventTypes.GetEnumerator() | Sort-Object Value -Descending
        
        foreach ($eventType in $sortedEventTypes) {
            $percentage = [math]::Round(($eventType.Value / $analysis.TotalEvents) * 100, 1)
            Write-Host ("  {0,-20} : {1,5} events ({2,5:F1}%)" -f $eventType.Key, $eventType.Value, $percentage) -ForegroundColor White
        }
    } else {
        Write-Host "  No events found" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "Process ID Summary:" -ForegroundColor Yellow
    Write-Host "------------------" -ForegroundColor Gray
    
    if ($analysis.PidEvents.Count -gt 0) {
        $sortedPids = $analysis.PidEvents.GetEnumerator() | Sort-Object Value -Descending
        
        foreach ($pid in $sortedPids) {
            $percentage = [math]::Round(($pid.Value / $analysis.TotalEvents) * 100, 1)
            
            # プロセス名を取得（可能な場合）
            $processName = ""
            try {
                $process = Get-Process -Id $pid.Key -ErrorAction SilentlyContinue
                if ($process) {
                    $processName = " ($($process.Name))"
                }
            } catch {
                # プロセスが既に終了している場合は無視
            }
            
            Write-Host ("  PID {0,-8}{1,-20} : {2,5} events ({3,5:F1}%)" -f $pid.Key, $processName, $pid.Value, $percentage) -ForegroundColor White
        }
    } else {
        Write-Host "  No process events found" -ForegroundColor Red
    }
    
    Write-Host ""
    Write-Host "===================================================" -ForegroundColor Cyan
    
    # 生のイベントを表示（オプション）
    if ($ShowRawEvents) {
        Write-Host ""
        Write-Host "Raw Events:" -ForegroundColor Yellow
        Write-Host "-----------" -ForegroundColor Gray
        Write-Host $eventsContent -ForegroundColor White
    }
    
    # 追加の分析情報
    Write-Host ""
    Write-Host "Additional Analysis:" -ForegroundColor Yellow
    Write-Host "-------------------" -ForegroundColor Gray
    
    # ファイル操作イベントの詳細
    $fileEvents = @("FileIO/Create", "FileIO/Write", "FileIO/Delete", "FileIO/Rename", "FileIO/SetInfo")
    $fileEventCount = 0
    foreach ($eventType in $fileEvents) {
        if ($analysis.EventTypes.ContainsKey($eventType)) {
            $fileEventCount += $analysis.EventTypes[$eventType]
        }
    }
    
    if ($fileEventCount -gt 0) {
        Write-Host ("  File I/O Events: {0} ({1:F1}% of total)" -f $fileEventCount, ([math]::Round(($fileEventCount / $analysis.TotalEvents) * 100, 1))) -ForegroundColor White
    }
    
    # プロセスイベントの詳細
    $processEvents = @("Process/Start", "Process/End")
    $processEventCount = 0
    foreach ($eventType in $processEvents) {
        if ($analysis.EventTypes.ContainsKey($eventType)) {
            $processEventCount += $analysis.EventTypes[$eventType]
        }
    }
    
    if ($processEventCount -gt 0) {
        Write-Host ("  Process Events: {0} ({1:F1}% of total)" -f $processEventCount, ([math]::Round(($processEventCount / $analysis.TotalEvents) * 100, 1))) -ForegroundColor White
    }
    
} else {
    Write-Host "ERROR: No event content to analyze" -ForegroundColor Red
}

Write-Host ""
Write-Host "Analysis complete." -ForegroundColor Green