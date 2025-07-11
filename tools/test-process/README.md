# ProcTail Test Process

ProcTail監視システムのテスト用プロセスです。自動化されたファイル・プロセス操作を実行し、ETWイベントの検出をテストします。

## 概要

このツールは手動でのnotepad操作に代わる自動化されたテストプロセスです：
- 予測可能なファイル操作パターン
- 制御可能な操作回数・間隔
- 詳細なログ出力
- JSON形式のレポート生成

## ビルド

```bash
# Linux/Unix用
go build -o test-process

# Windows用 (クロスコンパイル)
GOOS=windows GOARCH=amd64 go build -o test-process.exe
```

## 使用方法

### 基本構文
```
test-process [operation] [options]
```

### 操作タイプ
- `file-write`: ファイル書き込み操作
- `file-read`: ファイル読み込み操作  
- `file-delete`: ファイル削除操作
- `child-process`: 子プロセス作成
- `mixed`: 複数操作の組み合わせ
- `continuous`: 継続実行モード（指定時間継続的にファイル操作）

### オプション
- `--count N`: 操作回数 (デフォルト: 3)
- `--interval DURATION`: 操作間隔 (デフォルト: 1s)
- `--dir PATH`: 対象ディレクトリ (デフォルト: %TEMP%)
- `--verbose`: 詳細ログ出力
- `--json`: JSON形式で結果出力
- `--command CMD`: 実行するコマンド (child-process用)
- `--operations LIST`: 実行する操作のリスト (mixed用)
- `--wait`: 開始前にキー入力待機
- `--duration DURATION`: 継続実行時間 (continuous用、例: 30s, 5m)

## 使用例

### ファイル操作テスト
```bash
# 5回のファイル書き込み操作を1秒間隔で実行
./test-process file-write --count 5 --interval 1s --verbose

# ファイル読み込みテスト
./test-process file-read --count 3 --verbose

# ファイル削除テスト
./test-process file-delete --count 2 --verbose
```

### プロセス操作テスト
```bash
# 3回の子プロセス作成
./test-process child-process --count 3 --verbose

# カスタムコマンド実行
./test-process child-process --count 2 --command "cmd /c echo test" --verbose
```

### 複合操作テスト
```bash
# 書き込み、読み込み、削除の組み合わせ
./test-process mixed --count 3 --operations write,read,delete --verbose

# すべての操作タイプを実行
./test-process mixed --count 5 --operations write,read,delete,rename,process --verbose
```

### 継続実行テスト
```bash
# 30秒間継続的にファイル操作を実行
./test-process -duration 30s -interval 2s -verbose continuous

# 5分間継続的にファイル操作を実行（ETW長期テスト用）
./test-process -duration 5m -interval 1s -verbose continuous
```

### JSON出力
```bash
# JSON形式でレポートを出力
./test-process file-write --count 3 --json

# 出力例:
{
  "operation": "file-write",
  "config": {
    "count": 3,
    "interval": "1s",
    "dir": "/tmp",
    "verbose": false
  },
  "start_time": "2024-06-20T13:00:00Z",
  "end_time": "2024-06-20T13:00:03Z",
  "duration": "3s",
  "total_operations": 3,
  "successful_operations": 3,
  "failed_operations": 0,
  "process_id": 12345
}
```

## ProcTailテストでの使用

EndToEndSystemTests.csでは以下のように使用されます：

```csharp
// test-processを起動
var testProcess = await StartTestProcessAsync("file-write", 5);

// 監視対象に追加
await AddWatchTargetAsync(testProcess.Id, "test-process");

// プロセスの完了を待機
await testProcess.WaitForExitAsync();

// イベントを取得・検証
var events = await GetRecordedEventsAsync("test-process");
```

## 利点

### 従来のnotepadテストとの比較
| 項目 | notepad | test-process |
|------|---------|--------------|
| 手動操作 | 必要 | 不要 |
| 再現性 | 低い | 高い |
| 制御性 | 困難 | 完全制御 |
| CI/CD対応 | 不可 | 可能 |
| ログ出力 | なし | 詳細 |

### 特徴
- **完全自動化**: 人間の介入不要
- **予測可能**: 毎回同じ操作パターン
- **軽量**: 依存関係なし、高速起動
- **クロスプラットフォーム**: Windows/Linux対応
- **詳細レポート**: JSON形式での実行結果

## トラブルシューティング

### 権限エラー
```
Permission denied
```
- 実行権限を確認: `chmod +x test-process`
- 管理者権限で実行（Windowsの場合）

### ファイルが見つからない
```
test-process: command not found
```
- ビルドが完了しているか確認
- 正しいパスで実行しているか確認

### ETWイベントが検出されない
- ProcTailHostが管理者権限で実行されているか確認
- test-processが監視対象に正しく追加されているか確認
- ETWセッションが正常に動作しているか確認