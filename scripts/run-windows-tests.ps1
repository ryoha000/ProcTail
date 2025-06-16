# ProcTail Windows固有テスト実行スクリプト
# Usage: .\scripts\run-windows-tests.ps1 [Category] [Options]

param(
    [Parameter(Position=0)]
    [ValidateSet("admin", "windows", "etw", "system", "performance", "all")]
    [string]$Category = "admin",
    
    [Parameter()]
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [Parameter()]
    [switch]$Verbose,
    
    [Parameter()]
    [switch]$CollectCoverage,
    
    [Parameter()]
    [ValidateSet("console", "trx", "html")]
    [string]$Logger = "console",
    
    [Parameter()]
    [switch]$ElevateIfNeeded,
    
    [Parameter()]
    [switch]$Help
)

# 色付きログ出力関数
function Write-ColoredOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    
    $colors = @{
        "Red" = [ConsoleColor]::Red
        "Green" = [ConsoleColor]::Green
        "Yellow" = [ConsoleColor]::Yellow
        "Blue" = [ConsoleColor]::Blue
        "Cyan" = [ConsoleColor]::Cyan
        "White" = [ConsoleColor]::White
    }
    
    Write-Host $Message -ForegroundColor $colors[$Color]
}

function Write-Info {
    param([string]$Message)
    Write-ColoredOutput "[INFO] $Message" "Blue"
}

function Write-Success {
    param([string]$Message)
    Write-ColoredOutput "[SUCCESS] $Message" "Green"
}

function Write-Warning {
    param([string]$Message)
    Write-ColoredOutput "[WARNING] $Message" "Yellow"
}

function Write-Error {
    param([string]$Message)
    Write-ColoredOutput "[ERROR] $Message" "Red"
}

# ヘルプ表示
function Show-Help {
    @"
ProcTail Windows固有テスト実行スクリプト

Usage: .\scripts\run-windows-tests.ps1 [CATEGORY] [OPTIONS]

Categories:
  admin         - 管理者権限が必要なテスト (デフォルト)
  windows       - Windows固有テスト（管理者権限不要）
  etw           - ETWテストのみ
  system        - システムテスト（管理者権限必要）
  performance   - パフォーマンステスト
  all           - 全てのWindows固有テスト

Options:
  -Configuration <CONFIG>   ビルド設定 (Debug/Release)
  -Verbose                  詳細ログ出力
  -Logger <FORMAT>          ログ形式 (console/trx/html)
  -CollectCoverage          カバレッジ収集
  -ElevateIfNeeded          必要に応じて管理者権限で再実行
  -Help                     このヘルプを表示

Examples:
  .\scripts\run-windows-tests.ps1
  .\scripts\run-windows-tests.ps1 etw -Verbose
  .\scripts\run-windows-tests.ps1 all -Configuration Release
  .\scripts\run-windows-tests.ps1 admin -ElevateIfNeeded
"@
}

# 管理者権限チェック
function Test-Administrator {
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# 管理者権限で再実行
function Start-AsAdministrator {
    if (-not (Test-Administrator)) {
        Write-Warning "管理者権限が必要です。管理者権限で再実行します..."
        
        $arguments = @()
        $arguments += "-File", $MyInvocation.MyCommand.Path
        $arguments += "-Category", $Category
        $arguments += "-Configuration", $Configuration
        
        if ($Verbose) { $arguments += "-Verbose" }
        if ($CollectCoverage) { $arguments += "-CollectCoverage" }
        $arguments += "-Logger", $Logger
        
        try {
            Start-Process powershell.exe -ArgumentList $arguments -Verb RunAs -Wait
            exit 0
        }
        catch {
            Write-Error "管理者権限での実行に失敗しました: $($_.Exception.Message)"
            exit 1
        }
    }
}

# 環境チェック
function Test-Environment {
    Write-Info "環境チェックを実行中..."
    
    # Windowsプラットフォームチェック
    if (-not $IsWindows -and -not ($PSVersionTable.PSVersion.Major -le 5)) {
        Write-Error "このスクリプトはWindows環境でのみ実行可能です"
        exit 1
    }
    
    # .NET SDKチェック
    try {
        $dotnetVersion = & dotnet --version 2>$null
        Write-Info ".NET SDK バージョン: $dotnetVersion"
    }
    catch {
        Write-Error ".NET SDK がインストールされていません"
        exit 1
    }
    
    # プロジェクトルートチェック
    if (-not (Test-Path "ProcTail.sln")) {
        Write-Error "プロジェクトルートが見つかりません。スクリプトはプロジェクトルートから実行してください。"
        exit 1
    }
    
    # ETWアクセステスト（管理者権限テスト用）
    if ($Category -eq "admin" -or $Category -eq "etw" -or $Category -eq "system" -or $Category -eq "all") {
        if (-not (Test-Administrator)) {
            if ($ElevateIfNeeded) {
                Start-AsAdministrator
                return
            }
            else {
                Write-Error "このカテゴリのテストには管理者権限が必要です。-ElevateIfNeeded オプションを使用するか、管理者として実行してください。"
                exit 1
            }
        }
        
        Write-Info "管理者権限で実行中"
        
        # ETWアクセステスト
        try {
            Add-Type -TypeDefinition @"
using System;
using System.Diagnostics.Tracing;

public class TestEventSource : EventSource
{
    public static TestEventSource Log = new TestEventSource();
    public void TestEvent() { WriteEvent(1, "test"); }
}
"@
            [TestEventSource]::Log.TestEvent()
            Write-Info "ETWアクセステスト成功"
        }
        catch {
            Write-Warning "ETWアクセステストに失敗しました: $($_.Exception.Message)"
        }
    }
}

# テストフィルタ設定
function Get-TestFilter {
    switch ($Category) {
        "admin" {
            Write-Info "管理者権限が必要なテストを実行します"
            return "Category=RequiresAdmin"
        }
        "windows" {
            Write-Info "Windows固有テスト（管理者権限不要）を実行します"
            return "Category=RequiresWindows&Category!=RequiresAdmin"
        }
        "etw" {
            Write-Info "ETWテストを実行します"
            return "Category=RequiresAdmin&FullyQualifiedName~Etw"
        }
        "system" {
            Write-Info "システムテストを実行します"
            return "Category=RequiresAdmin|Category=EndToEnd"
        }
        "performance" {
            Write-Info "パフォーマンステストを実行します"
            return "Category=Performance"
        }
        "all" {
            Write-Info "すべてのWindows固有テストを実行します"
            return "Category=RequiresWindows|Category=RequiresAdmin|Category=EndToEnd|Category=Performance"
        }
        default {
            Write-Error "不明なカテゴリ: $Category"
            Show-Help
            exit 1
        }
    }
}

# プロジェクトビルド
function Invoke-Build {
    Write-Info "プロジェクトをビルド中... (Configuration: $Configuration)"
    
    $buildArgs = @(
        "build"
        "--configuration", $Configuration
        "--verbosity", "minimal"
    )
    
    & dotnet @buildArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Success "ビルド完了"
    }
    else {
        Write-Error "ビルドに失敗しました (Exit Code: $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
}

# テスト実行
function Invoke-Tests {
    Write-Info "テスト実行中..."
    
    $testFilter = Get-TestFilter
    
    $testArgs = @(
        "test"
        "--configuration", $Configuration
        "--no-build"
        "--verbosity", "minimal"
    )
    
    if ($testFilter) {
        $testArgs += "--filter", $testFilter
    }
    
    if ($Verbose) {
        $testArgs += "--verbosity", "detailed"
    }
    
    switch ($Logger) {
        "trx" {
            $testArgs += "--logger", "trx"
            $testArgs += "--results-directory", "./TestResults"
        }
        "html" {
            $testArgs += "--logger", "html"
            $testArgs += "--results-directory", "./TestResults"
        }
        "console" {
            $testArgs += "--logger", "console;verbosity=normal"
        }
    }
    
    if ($CollectCoverage) {
        Write-Info "コードカバレッジを収集します"
        $testArgs += "--collect:XPlat Code Coverage"
        
        # カバレッジディレクトリ作成
        $coverageDir = "./TestResults/Coverage"
        if (-not (Test-Path $coverageDir)) {
            New-Item -ItemType Directory -Path $coverageDir -Force | Out-Null
        }
    }
    
    Write-Info "実行コマンド: dotnet $($testArgs -join ' ')"
    
    & dotnet @testArgs
    $testResult = $LASTEXITCODE
    
    if ($testResult -eq 0) {
        Write-Success "すべてのテストが成功しました"
    }
    else {
        Write-Error "テストが失敗しました (Exit Code: $testResult)"
        exit $testResult
    }
}

# カバレッジレポート生成
function New-CoverageReport {
    if ($CollectCoverage) {
        Write-Info "カバレッジレポートを生成中..."
        
        # reportgeneratorの確認
        try {
            & reportgenerator --help 2>$null | Out-Null
        }
        catch {
            Write-Warning "reportgeneratorがインストールされていません。インストール中..."
            & dotnet tool install -g dotnet-reportgenerator-globaltool
        }
        
        # カバレッジファイル検索
        $coverageFiles = Get-ChildItem -Path "./TestResults" -Filter "coverage.cobertura.xml" -Recurse
        
        if ($coverageFiles.Count -gt 0) {
            $reportArgs = @(
                "-reports:./TestResults/**/coverage.cobertura.xml"
                "-targetdir:./TestResults/CoverageReport"
                "-reporttypes:Html;Badges"
                "-verbosity:Info"
            )
            
            & reportgenerator @reportArgs
            
            Write-Success "カバレッジレポートが生成されました: ./TestResults/CoverageReport/index.html"
            
            # カバレッジレポートを開く
            if (Test-Path "./TestResults/CoverageReport/index.html") {
                Write-Info "カバレッジレポートを開きますか? (y/N)"
                $response = Read-Host
                if ($response -eq "y" -or $response -eq "Y") {
                    Start-Process "./TestResults/CoverageReport/index.html"
                }
            }
        }
        else {
            Write-Warning "カバレッジファイルが見つかりませんでした"
        }
    }
}

# WSL連携情報表示
function Show-WSLIntegration {
    Write-Info "WSL連携情報:"
    Write-Info "  WSLからこのスクリプトを実行する場合:"
    Write-Info "    powershell.exe -File ./scripts/run-windows-tests.ps1 $Category"
    Write-Info "  WSLで実行可能なテスト:"
    Write-Info "    ./scripts/run-tests.sh all-safe"
}

# メイン実行
function main {
    if ($Help) {
        Show-Help
        exit 0
    }
    
    Write-Info "ProcTail Windows固有テストランナー開始"
    Write-Info "カテゴリ: $Category"
    Write-Info "設定: $Configuration"
    
    Test-Environment
    Invoke-Build
    Invoke-Tests
    New-CoverageReport
    Show-WSLIntegration
    
    Write-Success "テスト実行完了"
}

# エラーハンドリング
trap {
    Write-Error "予期しないエラーが発生しました: $($_.Exception.Message)"
    Write-Error "スタックトレース: $($_.ScriptStackTrace)"
    exit 1
}

# スクリプト実行
main