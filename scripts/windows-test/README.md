# ProcTail Windows Integration Tests

このディレクトリには、ProcTailのWindows環境での統合テストスクリプトが含まれています。

## 概要

ProcTailは、ETW (Event Tracing for Windows) を使用してプロセスのファイル操作とプロセスイベントを監視するWindowsワーカーサービスです。これらのテストスクリプトは、実際のWindows環境でHostプロセスとCLIの連携動作を検証します。

## 前提条件

- **Windows 10/11** (管理者権限)
- **.NET 8 Runtime**
- **PowerShell 5.1以上**
- **WSL** (メインスクリプト実行用)
- **Go** (test-process.exeのビルド用)

## テストスクリプト構成

### メインスクリプト

- **`run-windows-test.sh`** - WSL環境から実行するメインスクリプト
  - プロジェクトのビルド
  - ファイルのWindows環境へのコピー
  - 統合テストの実行

- **`integration-test.ps1`** - PowerShell統合テストスクリプト
  - Hostプロセスの起動
  - test-process.exeを使用した自動テスト
  - ファイル操作イベントの記録と検証

### 診断・ユーティリティスクリプト

- **`diagnose-host-startup.ps1`** - Host起動問題の詳細診断
  - ファイルシステムチェック
  - .NET Runtime環境確認
  - 依存関係DLLの存在確認
  - Host実行プロセスの詳細ログ

- **`cleanup-etw.ps1`** - ETWセッションの完全クリーンアップ
  - 既存のProcTailプロセス停止
  - 全てのProcTail関連ETWセッション停止
  - クリーンアップ確認

## 実行方法

### 1. 基本的な統合テスト

WSL環境から実行：

```bash
# プロジェクトルートディレクトリで
./scripts/windows-test/run-windows-test.sh
```

このスクリプトは以下を自動実行します：
1. プロジェクトのReleaseビルド
2. test-process.exeのビルド (Go言語)
3. ETWクリーンアップ（既存のHostプロセス停止とファイルロック解除）
4. バイナリファイルのWindows環境 (`C:/Temp/ProcTailTest/`) へのコピー
5. 管理者権限PowerShellでの統合テスト実行

### 2. 手動テスト

個別にコンポーネントをテストしたい場合：

```powershell
# PowerShell (管理者権限)
Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Force

# Host起動
Start-Process 'C:/Temp/ProcTailTest/host/win-x64/ProcTail.Host.exe' -WorkingDirectory 'C:/Temp/ProcTailTest/host' -PassThru

# CLIでのステータス確認
& 'C:/Temp/ProcTailTest/cli/win-x64/proctail.exe' status

# test-processを使用したテスト
$testProcess = Start-Process 'C:/Temp/ProcTailTest/tools/test-process.exe' -ArgumentList "-duration", "30s", "-interval", "2s", "-verbose", "-dir", "C:/Temp/ProcTailTest/TestFiles", "continuous" -PassThru
& 'C:/Temp/ProcTailTest/cli/win-x64/proctail.exe' add --pid $testProcess.Id --tag "test"

# イベント確認
& 'C:/Temp/ProcTailTest/cli/win-x64/proctail.exe' events --tag "test"
```

### 3. 問題診断

Host起動に問題がある場合：

```powershell
# PowerShell (管理者権限)
& 'C:/Temp/ProcTailScripts/diagnose-host-startup.ps1' -Verbose
```

### 4. ETWセッションクリーンアップ

ETWセッションが競合している場合や、テスト環境をリセットしたい場合：

```powershell
# PowerShell (管理者権限)
& 'C:/Temp/ProcTailScripts/cleanup-etw.ps1'
```

## テスト内容

### 統合テスト (integration-test.ps1)

1. **ETWクリーンアップ検証**
   - 事前クリーンアップの確認
   - 残存プロセスの追加クリーンアップ

2. **環境確認**
   - 管理者権限チェック
   - 必要ファイルの存在確認

3. **Host起動**
   - ProcTail Hostプロセスの起動
   - Named Pipeサーバーの初期化確認

4. **監視対象追加**
   - test-process.exeの起動
   - 監視対象としての登録

5. **自動ファイル操作テスト**
   - test-processによる連続的なファイル操作（作成、書き込み、削除）
   - ファイルI/Oイベントの自動記録と検証

6. **最終クリーンアップ**
   - プロセス停止
   - ETWセッション停止

### 診断テスト (diagnose-host-startup.ps1)

- ファイルシステム状態確認
- 設定ファイル (appsettings.json) 検証
- .NET Runtime環境確認
- 重要な依存関係DLL確認
- Host実行テストと詳細ログ出力

## トラブルシューティング

### よくある問題

1. **Host起動失敗**
   ```
   解決策: diagnose-host-startup.ps1 を実行して詳細診断
   ```

2. **CLIがHostに接続できない**
   ```
   確認項目:
   - Hostプロセスが実行中か
   - Named Pipeの接続状況
   - ファイアウォールの設定
   ```

3. **ETWイベントが記録されない**
   ```
   確認項目:
   - 管理者権限での実行
   - 監視対象プロセスのPID
   - ETWセッションの状態
   ```

4. **ファイルロックエラー**
   ```
   症状: Copy-Item でファイルがロックされているエラー
   原因: 既存のHostプロセスがDLLファイルを使用中
   解決策: run-windows-test.sh が自動的にETWクリーンアップを実行
   手動対処: cleanup-etw.ps1 を実行してからファイルコピー
   ```

5. **「実行ポリシー制限」エラー**
   ```
   解決策: Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Force
   ```

## ログファイル

テスト実行時のログは以下の場所に保存されます：

各テスト実行ごとに `C:/Temp/ProcTailTest/logs/{yyyyMMddHHmmss}/` ディレクトリが作成され、以下のログファイルが保存されます：

- **host.log**: ProcTail Hostプロセスのログ
- **test-process.log**: test-processの標準出力
- **test-process-error.log**: test-processのエラー出力

診断用のログ：
- **診断ログ**: `C:/ProcTail-Test-Logs/host-startup-[日時].log`

## 設定情報

### ディレクトリ構成

```
C:/Temp/ProcTailTest/
├── host/
│   ├── win-x64/
│   │   ├── ProcTail.Host.exe
│   │   ├── ProcTail.*.dll
│   │   └── ...
│   └── appsettings.json
├── cli/
│   └── win-x64/
│       ├── proctail.exe
│       ├── ProcTail.Core.dll
│       └── ...
├── tools/
│   └── test-process.exe
├── logs/
│   ├── test-process.log
│   └── test-process-error.log
└── TestFiles/
    └── (test-processが作成するファイル)

C:/Temp/ProcTailScripts/
├── integration-test.ps1
├── diagnose-host-startup.ps1
└── cleanup-etw.ps1
```

### Named Pipe設定

- **Pipe名**: `ProcTail`
- **接続方式**: Local Named Pipe
- **通信形式**: JSON over Named Pipe

## 既知の制限事項

### ETWイベントフィルタリング

現在、以下の理由でイベントが記録されない場合があります：

1. **プロセスフィルタリング**: システムプロセス（PID: 4等）のイベントは意図的に除外されます
2. **ETWセッション競合**: 複数のETWセッションが同時実行されると、イベントが正しく取得できない場合があります
3. **一時ディレクトリの除外**: 一部の一時ディレクトリでのファイル操作は除外される場合があります

### test-process.exeについて

test-process.exeは、ProcTailのテスト用に開発されたGo言語製のツールです：

- **連続モード**: `-duration` と `-interval` で指定した時間、定期的にファイル操作を実行
- **ファイル操作**: 作成、書き込み、読み込み、削除を自動実行
- **出力ディレクトリ**: `-dir` オプションで指定（デフォルト: 現在のディレクトリ）
- **詳細ログ**: `-verbose` オプションで詳細な操作ログを出力

## 参考

- [ETW (Event Tracing for Windows) ドキュメント](https://docs.microsoft.com/en-us/windows/win32/etw/event-tracing-portal)
- [Named Pipes ドキュメント](https://docs.microsoft.com/en-us/windows/win32/ipc/named-pipes)