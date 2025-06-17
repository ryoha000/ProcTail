# ProcTail CLI ユーザーガイド

ProcTail CLIは、Windows環境でETW (Event Tracing for Windows) を使用してプロセスのファイル操作と子プロセス作成を監視するコマンドラインツールです。

## 📋 目次

- [インストールと要件](#インストールと要件)
- [基本的な使い方](#基本的な使い方)
- [コマンド一覧](#コマンド一覧)
- [サービス管理](#サービス管理)
- [監視操作](#監視操作)
- [イベント取得と管理](#イベント取得と管理)
- [設定オプション](#設定オプション)
- [トラブルシューティング](#トラブルシューティング)

## 🚀 インストールと要件

### システム要件
- **OS**: Windows 10/11 または Windows Server 2019/2022
- **Framework**: .NET 8.0 Runtime
- **権限**: 管理者権限（ETW監視機能使用時）

### インストール
1. 最新リリースをダウンロード
2. 適切なフォルダに展開
3. 環境変数PATHに追加（オプション）

## 🔧 基本的な使い方

### ヘルプの表示
```bash
proctail --help
proctail <command> --help
```

### バージョン確認
```bash
proctail version
```

### 基本的なワークフロー
```bash
# 1. サービスをインストール（初回のみ）
proctail service install

# 2. サービスを開始
proctail service start

# 3. プロセス監視を追加
proctail add --pid 1234 --tag "my-app"

# 4. イベントを確認
proctail events --tag "my-app"

# 5. サービスを停止
proctail service stop
```

## 📖 コマンド一覧

### `proctail add` - 監視対象を追加

プロセスIDまたはプロセス名を指定して監視対象に追加します。

```bash
# プロセスIDで追加
proctail add --pid 1234 --tag "my-application"

# プロセス名で追加  
proctail add --name "notepad.exe" --tag "text-editor"

# 複数のプロセスを同じタグで監視
proctail add --name "chrome.exe" --tag "browser"
proctail add --name "msedge.exe" --tag "browser"
```

**オプション:**
- `--pid, --process-id <ID>`: 監視するプロセスID
- `--name, --process-name <NAME>`: 監視するプロセス名
- `--tag, -t <TAG>`: プロセスに付けるタグ名（必須）

### `proctail remove` - 監視対象を削除

指定したタグの監視を停止します。

```bash
proctail remove --tag "my-application"
```

**オプション:**
- `--tag, -t <TAG>`: 削除するタグ名（必須）

### `proctail list` - 監視対象一覧

現在監視中のプロセス一覧を表示します。

```bash
# テーブル形式で表示
proctail list

# JSON形式で表示
proctail list --format json

# CSV形式で表示
proctail list --format csv
```

**オプション:**
- `--format, -f <FORMAT>`: 出力形式（table, json, csv）

### `proctail events` - イベント表示

記録されたイベントを表示します。

```bash
# 最新50件を表示
proctail events --tag "my-app"

# 最新100件を表示
proctail events --tag "my-app" --count 100

# JSON形式で表示
proctail events --tag "my-app" --format json

# リアルタイムで表示（tail -f的な動作）
proctail events --tag "my-app" --follow
```

**オプション:**
- `--tag, -t <TAG>`: 取得するタグ名（必須）
- `--count, -n <COUNT>`: 取得するイベント数（デフォルト: 50）
- `--format, -f <FORMAT>`: 出力形式（table, json, csv）
- `--follow`: リアルタイムでイベントを表示

### `proctail status` - サービス状態確認

ProcTailサービスの状態を確認します。

```bash
# 詳細状態を表示
proctail status

# JSON形式で表示
proctail status --format json
```

### `proctail clear` - イベントクリア

指定したタグのイベント履歴をクリアします。

```bash
# 確認ありでクリア
proctail clear --tag "my-app"

# 確認なしでクリア
proctail clear --tag "my-app" --yes
```

**オプション:**
- `--tag, -t <TAG>`: クリアするタグ名（必須）
- `--yes, -y`: 確認をスキップ

## 🛠️ サービス管理

### `proctail service` - Windowsサービス管理

ProcTailをWindowsサービスとして管理します。

```bash
# サービスをインストール
proctail service install

# サービスを開始
proctail service start

# サービスを停止
proctail service stop

# サービスを再起動
proctail service restart

# サービスをアンインストール
proctail service uninstall
```

**注意:** サービス管理コマンドは管理者権限が必要です。

## 🎯 実践的な使用例

### 1. アプリケーション開発時のデバッグ

```bash
# Visual Studioプロセスを監視
proctail add --name "devenv.exe" --tag "vs-debug"

# ビルドプロセスを監視
proctail add --name "MSBuild.exe" --tag "vs-debug"

# リアルタイムでファイル操作を監視
proctail events --tag "vs-debug" --follow
```

### 2. セキュリティ監査

```bash
# 特定のサーバープロセスを監視
proctail add --name "w3wp.exe" --tag "iis-security"

# 1時間後にイベントを確認
proctail events --tag "iis-security" --count 1000

# CSVでエクスポートして分析
proctail events --tag "iis-security" --format csv > security_audit.csv
```

### 3. パフォーマンス分析

```bash
# データベースプロセスを監視
proctail add --name "sqlservr.exe" --tag "db-performance"

# 定期的にイベント数を確認
proctail status --format json

# メモリ使用量やイベント統計を確認
proctail events --tag "db-performance" --count 10
```

## ⚙️ グローバルオプション

すべてのコマンドで使用可能なオプション：

- `--verbose, -v`: 詳細な出力を表示
- `--config, -c <PATH>`: 設定ファイルのパス
- `--pipe-name, -p <NAME>`: Named Pipeの名前（デフォルト: ProcTail）
- `--no-uac`: UACプロンプトを無効にする

### 設定ファイル例

```json
{
  "ProcTail": {
    "ServiceName": "ProcTail",
    "DataDirectory": "C:\\ProcTail\\Data",
    "MaxEventsPerTag": 5000,
    "EventRetentionDays": 30
  },
  "Etw": {
    "SessionName": "ProcTailSession",
    "BufferSizeKB": 1024
  },
  "NamedPipe": {
    "PipeName": "ProcTailIPC",
    "MaxConcurrentConnections": 10
  }
}
```

## 🚨 管理者権限とUAC

### 権限が必要なコマンド
以下のコマンドは管理者権限が必要です：
- `proctail service *` - サービス管理
- ETW監視機能を使用する操作

### UACプロンプト
権限が不足している場合、自動的にUACプロンプトが表示されます：

```bash
# 管理者権限が必要な場合、UACが表示される
proctail service start

# UACを無効にして権限エラーで終了
proctail service start --no-uac
```

## 📊 出力形式

### Table形式（デフォルト）
```
+----------+----------------+--------------------+
| タグ名    | プロセスID      | 最終更新            |
+----------+----------------+--------------------+
| my-app   | 1234          | 2025-01-01 12:00:00 |
+----------+----------------+--------------------+
```

### JSON形式
```json
{
  "success": true,
  "data": [
    {
      "tag": "my-app",
      "processId": 1234,
      "lastUpdate": "2025-01-01T12:00:00Z"
    }
  ]
}
```

### CSV形式
```csv
Tag,ProcessId,LastUpdate
my-app,1234,2025-01-01T12:00:00Z
```

## 🔍 トラブルシューティング

### 一般的な問題

#### 1. 管理者権限エラー
```
エラー: このコマンドは管理者権限が必要です
```
**解決策:** 管理者権限でコマンドプロンプトを開いて実行してください。

#### 2. サービス接続エラー
```
エラー: ProcTailサービスに接続できません
```
**解決策:**
```bash
# サービスの状態を確認
proctail service status

# サービスを再起動
proctail service restart
```

#### 3. プロセスが見つからない
```
エラー: 指定されたプロセスが見つかりません
```
**解決策:**
```bash
# プロセス一覧を確認
tasklist | findstr "process-name"

# 正確なプロセス名またはPIDを指定
proctail add --pid 1234 --tag "my-app"
```

### ログの確認

Windowsイベントログを確認：
```bash
# PowerShellで確認
Get-WinEvent -LogName Application -Source ProcTail | Select-Object -First 10
```

### 設定の確認

現在の設定を確認：
```bash
proctail status --format json
```

## 📚 関連リソース

- **GitHub Repository**: [ProcTail Project](https://github.com/your-org/proctail)
- **Issues & Support**: [GitHub Issues](https://github.com/your-org/proctail/issues)
- **ETW Documentation**: [Microsoft ETW Documentation](https://docs.microsoft.com/en-us/windows/win32/etw/)

## 📄 ライセンス

ProcTail は MIT License の下で配布されています。

---

**注意:** このツールはWindows専用です。ETW機能を使用するため管理者権限が必要になる場合があります。