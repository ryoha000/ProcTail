#!/bin/bash

# ProcTail 統合テスト実行スクリプト (WSL + Windows)
# WSLからWindowsのPowerShellを呼び出して包括的なテストを実行

set -e

# 色付きログ出力
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

log_step() {
    echo -e "${CYAN}[STEP]${NC} $1"
}

# ヘルプ表示
show_help() {
    cat << EOF
ProcTail 統合テストランナー (WSL + Windows)

このスクリプトはWSL環境から実行し、プラットフォーム別にテストを実行します。

Usage: $0 [OPTIONS]

Options:
  --configuration, -c <CONFIG>  ビルド設定 (Debug/Release)
  --verbose, -v                 詳細ログ出力
  --coverage                    カバレッジ収集
  --skip-wsl                    WSLテストをスキップ
  --skip-windows                Windowsテストをスキップ
  --windows-only                Windowsテストのみ実行
  --help, -h                    このヘルプを表示

Test Execution Flow:
  1. WSL環境でユニット・統合テスト実行
  2. Windows環境でシステム・管理者権限テスト実行
  3. 結果の統合とレポート生成

Examples:
  $0                            # 全テスト実行
  $0 --verbose --coverage       # 詳細ログ + カバレッジ
  $0 --windows-only             # Windowsテストのみ
  $0 --skip-windows             # WSLテストのみ
EOF
}

# デフォルト値
CONFIGURATION="Debug"
VERBOSE=false
COLLECT_COVERAGE=false
SKIP_WSL=false
SKIP_WINDOWS=false
WINDOWS_ONLY=false

# 引数解析
while [[ $# -gt 0 ]]; do
    case $1 in
        --configuration|-c)
            CONFIGURATION="$2"
            shift 2
            ;;
        --verbose|-v)
            VERBOSE=true
            shift
            ;;
        --coverage)
            COLLECT_COVERAGE=true
            shift
            ;;
        --skip-wsl)
            SKIP_WSL=true
            shift
            ;;
        --skip-windows)
            SKIP_WINDOWS=true
            shift
            ;;
        --windows-only)
            WINDOWS_ONLY=true
            SKIP_WSL=true
            shift
            ;;
        --help|-h)
            show_help
            exit 0
            ;;
        *)
            log_error "不明なオプション: $1"
            show_help
            exit 1
            ;;
    esac
done

# 環境チェック
check_environment() {
    log_step "環境チェック中..."
    
    # WSL環境の確認
    if [[ -z "${WSL_DISTRO_NAME:-}" ]] && [[ ! (-f "/proc/version" && $(grep -c "Microsoft" /proc/version) -gt 0) ]]; then
        log_warning "WSL環境ではないようです。このスクリプトはWSLからの実行を推奨します。"
    else
        log_info "WSL環境を検出: ${WSL_DISTRO_NAME:-"Unknown"}"
    fi
    
    # .NET SDK確認
    if ! command -v dotnet &> /dev/null; then
        log_error ".NET SDK がインストールされていません"
        exit 1
    fi
    
    # PowerShellアクセス確認
    if ! command -v powershell.exe &> /dev/null; then
        log_error "PowerShell.exe にアクセスできません。WSL環境から実行していることを確認してください。"
        exit 1
    fi
    
    # プロジェクトルート確認
    if [[ ! -f "ProcTail.sln" ]]; then
        log_error "プロジェクトルートが見つかりません"
        exit 1
    fi
    
    log_success "環境チェック完了"
}

# WSLテスト実行
run_wsl_tests() {
    if [[ "$SKIP_WSL" == "true" ]]; then
        log_info "WSLテストをスキップします"
        return 0
    fi
    
    log_step "WSL環境でテスト実行中..."
    
    local wsl_args="all-safe --configuration $CONFIGURATION"
    
    if [[ "$VERBOSE" == "true" ]]; then
        wsl_args="$wsl_args --verbose"
    fi
    
    if [[ "$COLLECT_COVERAGE" == "true" ]]; then
        wsl_args="$wsl_args --collect-coverage"
    fi
    
    log_info "実行コマンド: ./scripts/run-tests.sh $wsl_args"
    
    if ./scripts/run-tests.sh $wsl_args; then
        log_success "WSLテスト完了"
        return 0
    else
        log_error "WSLテストが失敗しました"
        return 1
    fi
}

# Windowsテスト実行
run_windows_tests() {
    if [[ "$SKIP_WINDOWS" == "true" ]]; then
        log_info "Windowsテストをスキップします"
        return 0
    fi
    
    log_step "Windows環境でテスト実行中..."
    
    # Windows管理者権限テスト
    log_info "管理者権限テストを実行中..."
    local windows_args="-Category admin -Configuration $CONFIGURATION"
    
    if [[ "$VERBOSE" == "true" ]]; then
        windows_args="$windows_args -Verbose"
    fi
    
    if [[ "$COLLECT_COVERAGE" == "true" ]]; then
        windows_args="$windows_args -CollectCoverage"
    fi
    
    # 管理者権限で自動昇格
    windows_args="$windows_args -ElevateIfNeeded"
    
    log_info "実行コマンド: powershell.exe -File ./scripts/run-windows-tests.ps1 $windows_args"
    
    if powershell.exe -File ./scripts/run-windows-tests.ps1 $windows_args; then
        log_success "Windows管理者権限テスト完了"
    else
        log_error "Windows管理者権限テストが失敗しました"
        return 1
    fi
    
    # Windows一般テスト
    log_info "Windows一般テストを実行中..."
    local windows_general_args="-Category windows -Configuration $CONFIGURATION"
    
    if [[ "$VERBOSE" == "true" ]]; then
        windows_general_args="$windows_general_args -Verbose"
    fi
    
    log_info "実行コマンド: powershell.exe -File ./scripts/run-windows-tests.ps1 $windows_general_args"
    
    if powershell.exe -File ./scripts/run-windows-tests.ps1 $windows_general_args; then
        log_success "Windows一般テスト完了"
        return 0
    else
        log_error "Windows一般テストが失敗しました"
        return 1
    fi
}

# テスト結果統合
consolidate_results() {
    log_step "テスト結果を統合中..."
    
    local results_dir="./TestResults"
    local consolidated_dir="$results_dir/Consolidated"
    
    # 統合ディレクトリ作成
    mkdir -p "$consolidated_dir"
    
    # TRXファイルの統合
    local trx_files=$(find "$results_dir" -name "*.trx" -type f 2>/dev/null || true)
    if [[ -n "$trx_files" ]]; then
        log_info "TRXファイルを統合中..."
        cp $trx_files "$consolidated_dir/" 2>/dev/null || true
    fi
    
    # カバレッジレポートの統合
    if [[ "$COLLECT_COVERAGE" == "true" ]]; then
        log_info "カバレッジレポートを統合中..."
        
        local coverage_dirs=$(find "$results_dir" -name "CoverageReport" -type d 2>/dev/null || true)
        if [[ -n "$coverage_dirs" ]]; then
            local consolidated_coverage="$consolidated_dir/CoverageReport"
            mkdir -p "$consolidated_coverage"
            
            # 最新のカバレッジレポートをコピー
            local latest_coverage=$(ls -td $coverage_dirs | head -1)
            if [[ -n "$latest_coverage" ]]; then
                cp -r "$latest_coverage"/* "$consolidated_coverage/" 2>/dev/null || true
                log_info "統合カバレッジレポート: $consolidated_coverage/index.html"
            fi
        fi
    fi
    
    log_success "テスト結果統合完了: $consolidated_dir"
}

# サマリー表示
show_summary() {
    log_step "テスト実行サマリー"
    
    echo "----------------------------------------"
    echo "テスト実行設定:"
    echo "  設定: $CONFIGURATION"
    echo "  詳細ログ: $VERBOSE"
    echo "  カバレッジ: $COLLECT_COVERAGE"
    echo "  WSLテスト: $([[ "$SKIP_WSL" == "true" ]] && echo "スキップ" || echo "実行")"
    echo "  Windowsテスト: $([[ "$SKIP_WINDOWS" == "true" ]] && echo "スキップ" || echo "実行")"
    echo "----------------------------------------"
    
    if [[ -d "./TestResults" ]]; then
        echo "テスト結果ディレクトリ: ./TestResults"
        
        local trx_count=$(find ./TestResults -name "*.trx" -type f 2>/dev/null | wc -l)
        echo "  TRXファイル数: $trx_count"
        
        if [[ "$COLLECT_COVERAGE" == "true" ]]; then
            local coverage_reports=$(find ./TestResults -name "index.html" -path "*/CoverageReport/*" -type f 2>/dev/null | wc -l)
            echo "  カバレッジレポート数: $coverage_reports"
        fi
    fi
    
    echo "----------------------------------------"
}

# クリーンアップ
cleanup_on_exit() {
    log_info "クリーンアップ中..."
    # 必要に応じてテンポラリファイルの削除
}

# メイン実行
main() {
    log_info "ProcTail 統合テストランナー開始"
    
    # Ctrl+Cハンドリング
    trap cleanup_on_exit EXIT
    
    local start_time=$(date +%s)
    local wsl_result=0
    local windows_result=0
    
    check_environment
    
    # WSLテスト実行
    if ! run_wsl_tests; then
        wsl_result=1
    fi
    
    # Windowsテスト実行
    if ! run_windows_tests; then
        windows_result=1
    fi
    
    # 結果統合
    consolidate_results
    
    # 実行時間計算
    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local minutes=$((duration / 60))
    local seconds=$((duration % 60))
    
    # サマリー表示
    show_summary
    
    echo "実行時間: ${minutes}分${seconds}秒"
    
    # 最終結果
    if [[ $wsl_result -eq 0 && $windows_result -eq 0 ]]; then
        log_success "全てのテストが成功しました!"
        exit 0
    else
        log_error "一部のテストが失敗しました (WSL: $wsl_result, Windows: $windows_result)"
        exit 1
    fi
}

# スクリプト実行
main "$@"