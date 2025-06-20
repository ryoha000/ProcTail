# Windows環境でdotnet testを実行するスクリプト
param(
    [string]$TestFilter = "",
    [switch]$SkipEtw = $false,
    [switch]$AdminOnly = $false
)

# 管理者権限チェック
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")
if (-not $isAdmin -and $AdminOnly) {
    Write-Host "❌ このスクリプトは管理者権限で実行する必要があります" -ForegroundColor Red
    Write-Host "PowerShellを右クリックして「管理者として実行」を選択してください" -ForegroundColor Yellow
    exit 1
}

Write-Host "=== Windows環境でのdotnet testの実行 ===" -ForegroundColor Cyan
Write-Host "管理者権限: $isAdmin" -ForegroundColor Green

# テスト実行ディレクトリをセットアップ
$tempDir = "C:\Temp\ProcTailTest"
$sourceDir = "$tempDir\source"

if (Test-Path $tempDir) {
    Write-Host "既存のテストディレクトリをクリーンアップ中..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
}

Write-Host "ソースファイルをWindows環境にコピー中..." -ForegroundColor Yellow
New-Item -ItemType Directory -Path $sourceDir -Force | Out-Null

# WSLからWindows環境にファイルをコピー
$wslPath = "\\wsl.localhost\Ubuntu\home\ryoha\workspace\proctail"
robocopy $wslPath $sourceDir /E /XD bin obj .git /XF *.user *.tmp /NFL /NDL /NJH /NJS /NC /NS /NP

if ($LASTEXITCODE -ge 8) {
    Write-Host "❌ ファイルコピーに失敗しました" -ForegroundColor Red
    exit 1
}

Write-Host "✓ ファイルコピー完了" -ForegroundColor Green

# テストディレクトリに移動
Push-Location $sourceDir

# ビルド実行
Write-Host "プロジェクトをビルド中..." -ForegroundColor Yellow
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ ビルドに失敗しました" -ForegroundColor Red
    Pop-Location
    exit 1
}
Write-Host "✓ ビルド完了" -ForegroundColor Green

# テスト実行
Write-Host "テストを実行中..." -ForegroundColor Yellow

$testArgs = @(
    "test"
    "tests/ProcTail.System.Tests/ProcTail.System.Tests.csproj"
    "--configuration", "Release"
    "--logger", "console;verbosity=normal"
    "--no-build"
)

if ($TestFilter) {
    $testArgs += "--filter", $TestFilter
}

if ($SkipEtw) {
    # ETWテストをスキップ
    $testArgs += "--filter", "Category!=RequiresWindowsAndAdministrator"
} elseif ($AdminOnly) {
    # 管理者権限が必要なテストのみ実行
    $testArgs += "--filter", "Category=RequiresWindowsAndAdministrator"
}

Write-Host "実行コマンド: dotnet $($testArgs -join ' ')" -ForegroundColor Cyan

& dotnet @testArgs
$testExitCode = $LASTEXITCODE

if ($testExitCode -eq 0) {
    Write-Host "✓ すべてのテストが成功しました" -ForegroundColor Green
} else {
    Write-Host "❌ テストに失敗しました (ExitCode: $testExitCode)" -ForegroundColor Red
}

Pop-Location

Write-Host "=== テスト実行完了 ===" -ForegroundColor Cyan
Write-Host "一時ファイルを保持: $tempDir" -ForegroundColor Gray

# 管理者権限でETWセッションをクリーンアップ（必要に応じて）
if ($isAdmin -and -not $SkipEtw) {
    Write-Host "ETWセッションをクリーンアップ中..." -ForegroundColor Yellow
    $sessions = logman query -ets 2>$null | Where-Object { $_ -match "ProcTail" }
    foreach ($session in $sessions) {
        $sessionName = $session.Split()[0]
        if ($sessionName -and $sessionName.StartsWith("ProcTail")) {
            Write-Host "停止中: $sessionName" -ForegroundColor Gray
            logman stop $sessionName -ets 2>$null | Out-Null
        }
    }
    Write-Host "✓ ETWセッションクリーンアップ完了" -ForegroundColor Green
}

exit $testExitCode