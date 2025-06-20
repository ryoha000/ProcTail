# ProcTail Windows Integration Test
# テスト実行のみを担当（ビルドとファイルコピーはshell scriptで実行済み）

param(
    [string]$Tag = "test-notepad",
    [switch]$SkipCleanup,
    [switch]$KeepProcesses
)

Write-Host "===============================================" -ForegroundColor Yellow
Write-Host "   ProcTail Windows Integration Test" -ForegroundColor Yellow
Write-Host "===============================================" -ForegroundColor Yellow

# エラー発生時にスクリプトを停止
$ErrorActionPreference = "Stop"

# 管理者権限チェック
try {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host "❌ ERROR: This test requires administrator privileges." -ForegroundColor Red
        Write-Host "Please run PowerShell as Administrator" -ForegroundColor Yellow
        Read-Host "Press Enter to exit"
        exit 1
    }
} catch {
    Write-Host "❌ ERROR checking administrator privileges: $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "✅ Administrator privileges confirmed" -ForegroundColor Green

# テストファイルのパス設定
$testRoot = 'C:\Temp\ProcTailTest'
$hostDir = Join-Path $testRoot 'host'
$cliDir = Join-Path $testRoot 'cli'
$hostPath = Join-Path $hostDir 'ProcTail.Host.exe'
$cliPath = Join-Path $cliDir 'proctail.exe'

# ファイル存在確認
if (-not (Test-Path $hostPath)) {
    Write-Host "❌ ERROR: Host executable not found at: $hostPath" -ForegroundColor Red
    Write-Host "Please run the shell script first to build and copy files." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

if (-not (Test-Path $cliPath)) {
    Write-Host "❌ ERROR: CLI executable not found at: $cliPath" -ForegroundColor Red
    Write-Host "Please run the shell script first to build and copy files." -ForegroundColor Yellow
    Read-Host "Press Enter to exit"
    exit 1
}

Write-Host "✅ Required files found" -ForegroundColor Green

# メイン処理用の変数
$testSuccess = $false
$hostProcess = $null
$notepad = $null

try {
    # Step 1: ETWセッションクリーンアップ
    Write-Host "`n🧹 Step 1: ETW Session Cleanup..." -ForegroundColor Cyan
    
    # 既存のETWセッションを停止
    $etwSessions = @("ProcTail", "ProcTail-Dev", "NT Kernel Logger")
    foreach ($session in $etwSessions) {
        try {
            logman stop $session -ets 2>$null | Out-Null
            Write-Host "Stopped ETW session: $session" -ForegroundColor Gray
        }
        catch {
            # セッションが存在しない場合は無視
        }
    }
    
    # 既存のHostプロセスを停止
    Get-Process -Name "ProcTail.Host" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    
    Write-Host "✅ ETW cleanup completed" -ForegroundColor Green

    # Step 2: Host起動
    Write-Host "`n🚀 Step 2: Starting ProcTail Host..." -ForegroundColor Cyan
    
    $hostProcess = Start-Process -FilePath $hostPath -PassThru -WorkingDirectory $hostDir -WindowStyle Hidden
    Write-Host "Host started with PID: $($hostProcess.Id)" -ForegroundColor Green
    
    # Host初期化待機
    Write-Host "Waiting for Host initialization..." -ForegroundColor Gray
    Start-Sleep -Seconds 5
    
    # Hostの生存確認
    if ($hostProcess.HasExited) {
        Write-Host "❌ Host process has exited unexpectedly" -ForegroundColor Red
        throw "Host failed to start"
    }
    
    Write-Host "✅ Host is running" -ForegroundColor Green

    # Step 3: Notepad起動と監視開始
    Write-Host "`n📝 Step 3: Starting Notepad and monitoring..." -ForegroundColor Cyan
    
    $notepad = Start-Process -FilePath "notepad.exe" -PassThru
    $notepadPid = $notepad.Id
    Write-Host "Notepad started with PID: $notepadPid" -ForegroundColor Green
    
    # 監視対象に追加
    Write-Host "Adding Notepad to monitoring..." -ForegroundColor Gray
    $addResult = & $cliPath add --pid $notepadPid --tag $Tag 2>&1
    Write-Host "Add result: $addResult" -ForegroundColor Gray
    
    # 監視状態確認
    Start-Sleep -Seconds 2
    $status = & $cliPath status 2>&1
    Write-Host "Status: $status" -ForegroundColor Gray
    
    Write-Host "✅ Notepad monitoring started" -ForegroundColor Green

    # Step 4: ファイル保存テスト
    Write-Host "`n💾 Step 4: File save test..." -ForegroundColor Cyan
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host "  Please perform the following steps:" -ForegroundColor Yellow
    Write-Host "========================================" -ForegroundColor Yellow
    Write-Host "1. Switch to the Notepad window" -ForegroundColor White
    Write-Host "2. Type some text" -ForegroundColor White
    Write-Host "3. Press Ctrl+S to save" -ForegroundColor White
    Write-Host "4. Choose any location and filename" -ForegroundColor White
    Write-Host "5. Click Save" -ForegroundColor White
    Write-Host ""
    
    Read-Host "Press Enter after you have saved the file in Notepad"

    # Step 5: イベント確認
    Write-Host "`n🔍 Step 5: Checking captured events..." -ForegroundColor Cyan
    
    Start-Sleep -Seconds 2
    $events = & $cliPath events --tag $Tag 2>&1
    
    Write-Host ""
    Write-Host "================= EVENTS =================" -ForegroundColor Yellow
    Write-Host $events -ForegroundColor White
    Write-Host "==========================================" -ForegroundColor Yellow
    
    # イベントが取得できたかチェック
    if ($events -and $events.ToString().Contains("FileIO")) {
        Write-Host "✅ File events detected successfully!" -ForegroundColor Green
        $testSuccess = $true
    } else {
        Write-Host "⚠️  No file events detected" -ForegroundColor Yellow
        Write-Host "This might indicate a filtering or monitoring issue" -ForegroundColor Gray
        $testSuccess = $false
    }

} catch {
    Write-Host "❌ Test failed with error: $($_.Exception.Message)" -ForegroundColor Red
    $testSuccess = $false
} finally {
    # クリーンアップ
    Write-Host "`n🧹 Cleanup..." -ForegroundColor Cyan
    
    if (-not $KeepProcesses) {
        # Notepadを閉じる
        if ($notepad -and -not $notepad.HasExited) {
            $notepad | Stop-Process -Force -ErrorAction SilentlyContinue
            Write-Host "Notepad closed" -ForegroundColor Gray
        }
        
        # Hostを停止
        if ($hostProcess -and -not $hostProcess.HasExited) {
            $hostProcess | Stop-Process -Force -ErrorAction SilentlyContinue
            Write-Host "Host stopped" -ForegroundColor Gray
        }
        
        # ETWセッションを再度クリーンアップ
        foreach ($session in $etwSessions) {
            try {
                logman stop $session -ets 2>$null | Out-Null
            }
            catch {
                # 無視
            }
        }
    } else {
        Write-Host "Processes kept running (--KeepProcesses flag)" -ForegroundColor Yellow
    }
}

# 結果表示
Write-Host ""
Write-Host "===============================================" -ForegroundColor Yellow
if ($testSuccess) {
    Write-Host "🎉 TEST COMPLETED SUCCESSFULLY!" -ForegroundColor Green
} else {
    Write-Host "❌ TEST COMPLETED WITH ISSUES" -ForegroundColor Red
}
Write-Host "===============================================" -ForegroundColor Yellow
Write-Host ""
Write-Host "Test files are located at: $testRoot" -ForegroundColor Gray
Write-Host ""
Read-Host "Press Enter to exit"