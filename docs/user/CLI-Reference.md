# ProcTail CLI コマンドリファレンス

このドキュメントでは、ProcTail CLIツールの全コマンドと詳細オプションを説明します。

## 📋 目次

- [グローバルオプション](#グローバルオプション)
- [コマンド詳細](#コマンド詳細)
- [終了コード](#終了コード)
- [環境変数](#環境変数)
- [設定ファイル](#設定ファイル)

## 🌐 グローバルオプション

すべてのコマンドで使用可能なオプション：

| オプション | 短縮形 | 説明 | デフォルト値 |
|-----------|--------|------|-------------|
| `--verbose` | `-v` | 詳細な出力を表示 | false |
| `--config` | `-c` | 設定ファイルのパス | なし |
| `--pipe-name` | `-p` | Named Pipeの名前 | "ProcTail" |
| `--no-uac` | | UACプロンプトを無効化 | false |
| `--help` | `-h` | ヘルプを表示 | |
| `--version` | | バージョン情報を表示 | |

## 📝 コマンド詳細

### `proctail add`

監視対象プロセスを追加します。

#### 構文
```bash
proctail add [options]
```

#### オプション
| オプション | 短縮形 | 型 | 必須 | 説明 |
|-----------|--------|-----|------|------|
| `--process-id` | `--pid` | int | ✗ | 監視対象のプロセスID |
| `--process-name` | `--name` | string | ✗ | 監視対象のプロセス名 |
| `--tag` | `-t` | string | ✅ | プロセスに付けるタグ名 |

#### 使用例
```bash
# プロセスIDで指定
proctail add --pid 1234 --tag "notepad-session"

# プロセス名で指定
proctail add --name "chrome.exe" --tag "browser-monitoring"

# 複数のプロセスを同一タグで監視
proctail add --name "devenv.exe" --tag "development"
proctail add --name "MSBuild.exe" --tag "development"
```

#### 戻り値
- **成功**: 監視対象が正常に追加された
- **エラー**: プロセスが見つからない、または既に監視中

### `proctail remove`

指定したタグの監視を停止します。

#### 構文
```bash
proctail remove [options]
```

#### オプション
| オプション | 短縮形 | 型 | 必須 | 説明 |
|-----------|--------|-----|------|------|
| `--tag` | `-t` | string | ✅ | 削除するタグ名 |

#### 使用例
```bash
proctail remove --tag "notepad-session"
```

### `proctail list`

現在の監視対象一覧を表示します。

#### 構文
```bash
proctail list [options]
```

#### オプション
| オプション | 短縮形 | 型 | 必須 | 説明 |
|-----------|--------|-----|------|------|
| `--format` | `-f` | string | ✗ | 出力形式: table, json, csv |

#### 使用例
```bash
# デフォルト（テーブル形式）
proctail list

# JSON形式で出力
proctail list --format json

# CSV形式で出力（Excel等での分析用）
proctail list --format csv > monitoring_targets.csv
```

#### 出力例

**Table形式:**
```
+------------------+------------+----------------------+-------------+
| タグ名            | プロセスID  | プロセス名            | 開始時刻     |
+------------------+------------+----------------------+-------------+
| browser          | 2468       | chrome.exe          | 10:30:15    |
| development      | 1234       | devenv.exe          | 09:15:30    |
| development      | 3456       | MSBuild.exe         | 09:45:22    |
+------------------+------------+----------------------+-------------+
```

**JSON形式:**
```json
{
  "success": true,
  "timestamp": "2025-01-01T12:00:00Z",
  "data": [
    {
      "tag": "browser",
      "processId": 2468,
      "processName": "chrome.exe",
      "startTime": "2025-01-01T10:30:15Z",
      "isRunning": true
    }
  ]
}
```

### `proctail events`

記録されたイベントを表示します。

#### 構文
```bash
proctail events [options]
```

#### オプション
| オプション | 短縮形 | 型 | 必須 | 説明 |
|-----------|--------|-----|------|------|
| `--tag` | `-t` | string | ✅ | 取得するタグ名 |
| `--count` | `-n` | int | ✗ | 取得するイベント数（デフォルト: 50） |
| `--format` | `-f` | string | ✗ | 出力形式: table, json, csv |
| `--follow` | | bool | ✗ | リアルタイムでイベントを表示 |

#### 使用例
```bash
# 最新50件のイベントを表示
proctail events --tag "development"

# 最新200件をJSON形式で表示
proctail events --tag "development" --count 200 --format json

# リアルタイム監視（Ctrl+Cで終了）
proctail events --tag "development" --follow

# CSVでエクスポート
proctail events --tag "security-audit" --count 1000 --format csv > security_events.csv
```

#### イベント出力例

**Table形式:**
```
+--------------------+----------+------------------+--------------------------------+
| 時刻               | 種別      | プロセスID        | 詳細                           |
+--------------------+----------+------------------+--------------------------------+
| 12:34:56.789       | FileOp   | 1234             | C:\temp\test.txt (Create)     |
| 12:34:57.012       | FileOp   | 1234             | C:\temp\test.txt (Write)      |
| 12:34:58.345       | Process  | 2468             | notepad.exe (Start)           |
+--------------------+----------+------------------+--------------------------------+
```

**JSON形式:**
```json
{
  "success": true,
  "tag": "development",
  "eventCount": 3,
  "events": [
    {
      "timestamp": "2025-01-01T12:34:56.789Z",
      "eventType": "FileOperation",
      "processId": 1234,
      "processName": "devenv.exe",
      "operation": "Create",
      "filePath": "C:\\temp\\test.txt",
      "fileSize": 0
    },
    {
      "timestamp": "2025-01-01T12:34:58.345Z",
      "eventType": "ProcessStart",
      "processId": 2468,
      "processName": "notepad.exe",
      "parentProcessId": 1234,
      "commandLine": "notepad.exe C:\\temp\\test.txt"
    }
  ]
}
```

### `proctail status`

ProcTailサービスの状態を表示します。

#### 構文
```bash
proctail status [options]
```

#### オプション
| オプション | 短縮形 | 型 | 必須 | 説明 |
|-----------|--------|-----|------|------|
| `--format` | `-f` | string | ✗ | 出力形式: table, json |

#### 出力例

**Table形式:**
```
ProcTail Service Status
=======================
Status: Running
Uptime: 2 hours, 15 minutes
Active Targets: 3 tags, 5 processes
Total Events: 1,247
Memory Usage: 45.2 MB
ETW Session: Active
Named Pipe: Connected (2 clients)
```

**JSON形式:**
```json
{
  "success": true,
  "service": {
    "status": "Running",
    "uptime": "02:15:30",
    "version": "1.0.0",
    "startTime": "2025-01-01T10:00:00Z"
  },
  "monitoring": {
    "activeTags": 3,
    "activeProcesses": 5,
    "totalEvents": 1247,
    "etwSessionActive": true
  },
  "resources": {
    "memoryUsageMB": 45.2,
    "cpuUsagePercent": 2.1
  },
  "ipc": {
    "namedPipeActive": true,
    "connectedClients": 2
  }
}
```

### `proctail clear`

指定したタグのイベント履歴をクリアします。

#### 構文
```bash
proctail clear [options]
```

#### オプション
| オプション | 短縮形 | 型 | 必須 | 説明 |
|-----------|--------|-----|------|------|
| `--tag` | `-t` | string | ✅ | クリアするタグ名 |
| `--yes` | `-y` | bool | ✗ | 確認をスキップ |

#### 使用例
```bash
# 確認ありでクリア
proctail clear --tag "development"

# 確認なしでクリア（スクリプト用）
proctail clear --tag "old-session" --yes
```

### `proctail service`

Windowsサービスの管理を行います。

#### 構文
```bash
proctail service <command>
```

#### サブコマンド

##### `proctail service install`
ProcTailをWindowsサービスとしてインストールします。

**必要権限:** 管理者権限

```bash
proctail service install
```

##### `proctail service start`
ProcTailサービスを開始します。

**必要権限:** 管理者権限

```bash
proctail service start
```

##### `proctail service stop`
ProcTailサービスを停止します。

**必要権限:** 管理者権限

```bash
proctail service stop
```

##### `proctail service restart`
ProcTailサービスを再起動します。

**必要権限:** 管理者権限

```bash
proctail service restart
```

##### `proctail service uninstall`
ProcTailサービスをアンインストールします。

**必要権限:** 管理者権限

```bash
proctail service uninstall
```

### `proctail version`

バージョン情報を表示します。

#### 構文
```bash
proctail version
```

#### 出力例
```
ProcTail CLI Tool
バージョン: 1.0.0.0
ファイルバージョン: 1.0.0.0
ビルド日時: 2025-01-01 12:00:00
実行環境: .NET 8.0.13
OS: Microsoft Windows NT 10.0.26100.0
アーキテクチャ: X64

Copyright (c) 2025 ProcTail Project
ETWを使用したプロセス・ファイル操作監視ツール
```

## 🚦 終了コード

| コード | 説明 |
|--------|------|
| 0 | 正常終了 |
| 1 | 一般的なエラー |
| 2 | 権限不足エラー |
| 3 | サービス接続エラー |
| 4 | 無効な引数エラー |

## 🌍 環境変数

| 変数名 | 説明 | デフォルト値 |
|--------|------|-------------|
| `PROCTAIL_CONFIG` | 設定ファイルのパス | なし |
| `PROCTAIL_PIPE_NAME` | Named Pipe名 | "ProcTail" |
| `PROCTAIL_LOG_LEVEL` | ログレベル | "Information" |

## ⚙️ 設定ファイル

### 設定ファイルの場所
1. `--config`オプションで指定されたパス
2. `%PROCTAIL_CONFIG%`環境変数のパス
3. `appsettings.json`（実行ディレクトリ内）
4. `%ProgramData%\ProcTail\appsettings.json`

### 設定ファイルの例
```json
{
  "ProcTail": {
    "ServiceName": "ProcTail",
    "DisplayName": "ProcTail Process Monitor",
    "Description": "Monitors process file operations and child process creation using ETW",
    "DataDirectory": "C:\\ProcData\\ProcTail",
    "MaxEventsPerTag": 10000,
    "EventRetentionDays": 30,
    "EnableMetrics": true,
    "MetricsInterval": "00:01:00",
    "HealthCheckInterval": "00:00:30"
  },
  "Etw": {
    "SessionName": "ProcTailSession",
    "BufferSizeKB": 1024,
    "MaxFileSize": 100,
    "LogFileMode": "Sequential"
  },
  "NamedPipe": {
    "PipeName": "ProcTailIPC",
    "MaxConcurrentConnections": 10,
    "BufferSize": 4096,
    "ResponseTimeoutSeconds": 30,
    "ConnectionTimeoutSeconds": 10
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "ProcTail": "Debug"
    },
    "Console": {
      "IncludeScopes": false
    },
    "EventLog": {
      "LogLevel": "Warning"
    }
  }
}
```

### 設定項目の説明

#### ProcTail設定
- `MaxEventsPerTag`: タグごとの最大イベント保持数
- `EventRetentionDays`: イベントの保持日数
- `DataDirectory`: データファイルの保存ディレクトリ
- `EnableMetrics`: メトリクス収集の有効/無効
- `MetricsInterval`: メトリクス収集間隔
- `HealthCheckInterval`: ヘルスチェック間隔

#### ETW設定
- `SessionName`: ETWセッション名
- `BufferSizeKB`: ETWバッファサイズ（KB）
- `MaxFileSize`: ログファイルの最大サイズ（MB）
- `LogFileMode`: ログファイルモード

#### Named Pipe設定
- `PipeName`: パイプ名
- `MaxConcurrentConnections`: 最大同時接続数
- `BufferSize`: バッファサイズ（バイト）
- `ResponseTimeoutSeconds`: レスポンスタイムアウト（秒）
- `ConnectionTimeoutSeconds`: 接続タイムアウト（秒）

## 🔧 高度な使用方法

### バッチスクリプトでの使用

```batch
@echo off
echo ProcTail監視開始スクリプト

rem サービス状態確認
proctail status >nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo サービスが開始されていません。開始します...
    proctail service start
    if %ERRORLEVEL% neq 0 (
        echo サービス開始に失敗しました。
        exit /b 1
    )
)

rem 監視対象を追加
echo 監視対象を追加中...
proctail add --name "notepad.exe" --tag "test-session"
proctail add --name "calc.exe" --tag "test-session"

echo 監視が開始されました。Ctrl+Cで終了します。
proctail events --tag "test-session" --follow
```

### PowerShellでの使用

```powershell
# ProcTail監視の自動化スクリプト

# 監視対象プロセスの定義
$MonitoringTargets = @(
    @{ Name = "chrome.exe"; Tag = "browsers" },
    @{ Name = "msedge.exe"; Tag = "browsers" },
    @{ Name = "devenv.exe"; Tag = "development" },
    @{ Name = "code.exe"; Tag = "development" }
)

# サービスの開始確認
try {
    $status = proctail status --format json | ConvertFrom-Json
    if (-not $status.success) {
        Write-Host "サービスを開始しています..." -ForegroundColor Yellow
        proctail service start
    }
} catch {
    Write-Error "サービスの状態確認に失敗しました: $_"
    exit 1
}

# 監視対象の追加
foreach ($target in $MonitoringTargets) {
    Write-Host "監視対象を追加: $($target.Name) -> $($target.Tag)" -ForegroundColor Green
    proctail add --name $target.Name --tag $target.Tag
}

# 監視結果の定期取得
while ($true) {
    Start-Sleep -Seconds 60
    
    foreach ($tag in ($MonitoringTargets.Tag | Select-Object -Unique)) {
        $events = proctail events --tag $tag --count 10 --format json | ConvertFrom-Json
        if ($events.eventCount -gt 0) {
            Write-Host "[$tag] 新しいイベント: $($events.eventCount)件" -ForegroundColor Cyan
        }
    }
}
```

### JSON出力のパース例

```powershell
# イベントをJSONで取得して処理
$events = proctail events --tag "security" --format json | ConvertFrom-Json

# ファイル操作イベントのみをフィルタ
$fileEvents = $events.events | Where-Object { $_.eventType -eq "FileOperation" }

# 書き込み操作のみを表示
$writeEvents = $fileEvents | Where-Object { $_.operation -eq "Write" }

Write-Host "書き込み操作: $($writeEvents.Count)件"
$writeEvents | Format-Table timestamp, processName, filePath
```

---

**注意:** このドキュメントは ProcTail CLI v1.0.0 に基づいています。最新の情報については公式ドキュメントを参照してください。