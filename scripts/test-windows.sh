#!/bin/bash

# ProcTail Windows統合テストスクリプト
# WSLでビルドし、Windows PowerShell経由でテストを実行

set -e

# スクリプトのディレクトリを取得
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
WINDOWS_TEST_DIR="$SCRIPT_DIR/windows-test"

# カラー出力用
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

# エラーハンドリング
cleanup_on_error() {
    log_error "テスト中にエラーが発生しました。クリーンアップを実行します..."
    cd "$PROJECT_ROOT"
    if [ -f "$WINDOWS_TEST_DIR/cleanup.sh" ]; then
        bash "$WINDOWS_TEST_DIR/cleanup.sh"
    fi
    exit 1
}

trap cleanup_on_error ERR

# メイン処理
main() {
    log_info "ProcTail Windows統合テストを開始します"
    log_info "プロジェクトルート: $PROJECT_ROOT"
    log_info "テストスクリプトディレクトリ: $WINDOWS_TEST_DIR"

    # プロジェクトルートに移動
    cd "$PROJECT_ROOT"

    # 前提条件チェック
    log_info "前提条件をチェック中..."
    
    # WSL環境チェック
    if ! grep -qi microsoft /proc/version 2>/dev/null; then
        log_error "このスクリプトはWSL環境で実行してください"
        exit 1
    fi
    log_success "WSL環境を確認しました"

    # .NET SDKチェック
    if ! command -v dotnet &> /dev/null; then
        log_error ".NET SDKが見つかりません"
        exit 1
    fi
    log_success ".NET SDK $(dotnet --version) を確認しました"

    # PowerShell.exeアクセスチェック
    if ! command -v powershell.exe &> /dev/null; then
        log_error "powershell.exe にアクセスできません"
        exit 1
    fi
    log_success "PowerShell.exe へのアクセスを確認しました"

    # テストスクリプトディレクトリの存在チェック
    if [ ! -d "$WINDOWS_TEST_DIR" ]; then
        log_error "テストスクリプトディレクトリが見つかりません: $WINDOWS_TEST_DIR"
        exit 1
    fi

    # 1. ビルド実行
    log_info "=== ビルドフェーズ ==="
    if ! bash "$WINDOWS_TEST_DIR/build.sh"; then
        log_error "ビルドに失敗しました"
        exit 1
    fi
    log_success "ビルドが完了しました"

    # 2. テスト実行
    log_info "=== テスト実行フェーズ ==="
    if ! bash "$WINDOWS_TEST_DIR/run-test.sh"; then
        log_error "テストの実行に失敗しました"
        exit 1
    fi
    log_success "テストが完了しました"

    # 3. 結果確認
    log_info "=== 結果確認フェーズ ==="
    RESULTS_FILE="$PROJECT_ROOT/test-results.json"
    if [ -f "$RESULTS_FILE" ]; then
        log_success "テスト結果ファイルが見つかりました: $RESULTS_FILE"
        
        # 結果の簡単な表示
        if command -v jq &> /dev/null; then
            log_info "テスト結果の概要:"
            jq -r '.summary // "結果サマリーなし"' "$RESULTS_FILE" 2>/dev/null || echo "結果の解析に失敗"
        else
            log_info "jqがインストールされていないため、生の結果を表示:"
            head -20 "$RESULTS_FILE"
        fi
    else
        log_warning "テスト結果ファイルが見つかりません"
    fi

    # 4. クリーンアップ
    log_info "=== クリーンアップフェーズ ==="
    if ! bash "$WINDOWS_TEST_DIR/cleanup.sh"; then
        log_warning "クリーンアップでエラーが発生しましたが、継続します"
    fi
    log_success "クリーンアップが完了しました"

    # 5. 最終結果
    echo
    log_success "=========================================="
    log_success "ProcTail Windows統合テストが完了しました"
    log_success "=========================================="
    
    if [ -f "$RESULTS_FILE" ]; then
        log_info "詳細な結果は以下のファイルを確認してください: $RESULTS_FILE"
    fi
    
    echo
    log_info "テストに使用したコンポーネント:"
    log_info "  - Host: $(ls -la publish/host/ProcTail.Host.exe 2>/dev/null || echo '未生成')"
    log_info "  - CLI: $(ls -la publish/cli/ProcTail.Cli.exe 2>/dev/null || echo '未生成')"
}

# スクリプト実行
main "$@"