#!/bin/bash

# ProcTail テスト実行スクリプト (WSL/Linux用)
# Usage: ./scripts/run-tests.sh [category] [options]

set -e

# 色付きログ出力
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# ログ関数
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

# ヘルプ表示
show_help() {
    cat << EOF
ProcTail Test Runner (WSL/Linux版)

Usage: $0 [CATEGORY] [OPTIONS]

Categories:
  unit          - ユニットテストのみ実行 (デフォルト)
  integration   - 統合テストのみ実行
  cross-platform - WSL/Linuxで実行可能なテスト
  all-safe      - Windows依存テストを除外して全実行
  coverage      - カバレッジ付きでテスト実行

Options:
  --verbose, -v     詳細ログ出力
  --configuration, -c <CONFIG>  ビルド設定 (Debug/Release)
  --logger <FORMAT>  ログ形式 (console/trx/html)
  --collect-coverage カバレッジ収集
  --help, -h        このヘルプを表示

Examples:
  $0                           # ユニットテストのみ実行
  $0 integration               # 統合テストのみ実行
  $0 all-safe --verbose        # WSLで安全に実行可能な全テスト
  $0 coverage                  # カバレッジ付きテスト実行
  $0 unit -c Release          # Releaseビルドでユニットテスト
EOF
}

# デフォルト値
CATEGORY="unit"
CONFIGURATION="Debug"
VERBOSE=false
COLLECT_COVERAGE=false
LOGGER="console"
ADDITIONAL_ARGS=""

# 引数解析
while [[ $# -gt 0 ]]; do
    case $1 in
        unit|integration|cross-platform|all-safe|coverage)
            CATEGORY="$1"
            shift
            ;;
        --verbose|-v)
            VERBOSE=true
            shift
            ;;
        --configuration|-c)
            CONFIGURATION="$2"
            shift 2
            ;;
        --logger)
            LOGGER="$2"
            shift 2
            ;;
        --collect-coverage)
            COLLECT_COVERAGE=true
            shift
            ;;
        --help|-h)
            show_help
            exit 0
            ;;
        *)
            ADDITIONAL_ARGS="$ADDITIONAL_ARGS $1"
            shift
            ;;
    esac
done

# 環境チェック
check_environment() {
    log_info "環境チェックを実行中..."
    
    # .NET SDKの確認
    if ! command -v dotnet &> /dev/null; then
        log_error ".NET SDK がインストールされていません"
        exit 1
    fi
    
    local dotnet_version=$(dotnet --version)
    log_info ".NET SDK バージョン: $dotnet_version"
    
    # WSL環境の検出
    if [[ -n "${WSL_DISTRO_NAME:-}" ]] || [[ -f "/proc/version" && $(grep -c "Microsoft" /proc/version) -gt 0 ]]; then
        log_info "WSL環境を検出しました"
        export TEST_ENVIRONMENT="WSL"
    else
        log_info "Linux環境で実行中"
        export TEST_ENVIRONMENT="Linux"
    fi
    
    # プロジェクトルートの確認
    if [[ ! -f "ProcTail.sln" ]]; then
        log_error "プロジェクトルートが見つかりません。スクリプトはプロジェクトルートから実行してください。"
        exit 1
    fi
}

# テストフィルタ設定
setup_test_filter() {
    case "$CATEGORY" in
        "unit")
            TEST_FILTER="Category=Unit"
            log_info "ユニットテストを実行します"
            ;;
        "integration")
            TEST_FILTER="Category=Integration"
            log_info "統合テストを実行します"
            ;;
        "cross-platform")
            TEST_FILTER="Category!=RequiresWindows&Category!=RequiresAdmin"
            log_info "クロスプラットフォーム対応テストを実行します"
            ;;
        "all-safe")
            TEST_FILTER="Category!=RequiresWindows&Category!=RequiresAdmin&Category!=EndToEnd"
            log_info "WSL/Linuxで安全に実行可能な全テストを実行します"
            ;;
        "coverage")
            TEST_FILTER="Category!=RequiresWindows&Category!=RequiresAdmin"
            COLLECT_COVERAGE=true
            log_info "カバレッジ付きテストを実行します"
            ;;
        *)
            log_error "不明なカテゴリ: $CATEGORY"
            show_help
            exit 1
            ;;
    esac
}

# プロジェクトビルド
build_projects() {
    log_info "プロジェクトをビルド中... (Configuration: $CONFIGURATION)"
    
    dotnet build --configuration "$CONFIGURATION" --verbosity minimal
    
    if [[ $? -eq 0 ]]; then
        log_success "ビルド完了"
    else
        log_error "ビルドに失敗しました"
        exit 1
    fi
}

# テスト実行
run_tests() {
    log_info "テスト実行中..."
    
    # 基本的なテスト引数
    local test_args="--configuration $CONFIGURATION --no-build --verbosity minimal"
    
    # フィルタ設定
    if [[ -n "$TEST_FILTER" ]]; then
        test_args="$test_args --filter \"$TEST_FILTER\""
    fi
    
    # 詳細ログ (基本verbosityを上書き)
    if [[ "$VERBOSE" == "true" ]]; then
        test_args="${test_args/--verbosity minimal/--verbosity detailed}"
    fi
    
    # ログフォーマット
    case "$LOGGER" in
        "trx")
            test_args="$test_args --logger trx --results-directory ./TestResults"
            ;;
        "html")
            test_args="$test_args --logger html --results-directory ./TestResults"
            ;;
        "console")
            test_args="$test_args --logger console;verbosity=normal"
            ;;
    esac
    
    # カバレッジ収集
    if [[ "$COLLECT_COVERAGE" == "true" ]]; then
        log_info "コードカバレッジを収集します"
        test_args="$test_args --collect:\"XPlat Code Coverage\""
        
        # coverletの設定
        export DOTNET_COVERAGE_DIR="./TestResults/Coverage"
        mkdir -p "$DOTNET_COVERAGE_DIR"
    fi
    
    # 追加引数
    if [[ -n "$ADDITIONAL_ARGS" ]]; then
        test_args="$test_args $ADDITIONAL_ARGS"
    fi
    
    # テスト実行
    log_info "実行コマンド: dotnet test $test_args"
    
    eval "dotnet test $test_args"
    local test_result=$?
    
    if [[ $test_result -eq 0 ]]; then
        log_success "すべてのテストが成功しました"
    else
        log_error "テストが失敗しました (Exit Code: $test_result)"
        return $test_result
    fi
}

# カバレッジレポート生成
generate_coverage_report() {
    if [[ "$COLLECT_COVERAGE" == "true" ]]; then
        log_info "カバレッジレポートを生成中..."
        
        # reportgeneratorがインストールされているかチェック
        if ! command -v reportgenerator &> /dev/null; then
            log_warning "reportgeneratorがインストールされていません。インストール中..."
            dotnet tool install -g dotnet-reportgenerator-globaltool
        fi
        
        # カバレッジファイルの検索
        local coverage_files=$(find ./TestResults -name "coverage.cobertura.xml" -type f)
        
        if [[ -n "$coverage_files" ]]; then
            reportgenerator \
                -reports:"./TestResults/**/coverage.cobertura.xml" \
                -targetdir:"./TestResults/CoverageReport" \
                -reporttypes:"Html;Badges" \
                -verbosity:Info
            
            log_success "カバレッジレポートが生成されました: ./TestResults/CoverageReport/index.html"
        else
            log_warning "カバレッジファイルが見つかりませんでした"
        fi
    fi
}

# Windows固有テストの警告表示
show_windows_test_warning() {
    if [[ "$CATEGORY" == "all-safe" || "$CATEGORY" == "cross-platform" ]]; then
        log_warning "Windows固有テスト（ETW、Named Pipes、管理者権限テスト）はスキップされます"
        log_info "Windows固有テストを実行する場合は、Windows環境で以下を実行してください:"
        log_info "  powershell.exe -Command \"./scripts/run-windows-tests.ps1\""
    fi
}

# クリーンアップ
cleanup() {
    log_info "クリーンアップ中..."
    # 必要に応じてテンポラリファイルの削除など
}

# メイン実行
main() {
    log_info "ProcTail テストランナー開始 (WSL/Linux版)"
    log_info "カテゴリ: $CATEGORY"
    log_info "設定: $CONFIGURATION"
    
    check_environment
    setup_test_filter
    show_windows_test_warning
    
    # Ctrl+Cハンドリング
    trap cleanup EXIT
    
    build_projects
    run_tests
    generate_coverage_report
    
    log_success "テスト実行完了"
}

# スクリプト実行
main "$@"