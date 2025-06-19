# ProcTail Windows Integration Test

このディレクトリには、ProcTailのWindows機能をテストするためのPowerShellスクリプトが含まれています。

## 前提条件

- Windows環境
- 管理者権限でのPowerShell実行
- ProcTailアプリケーションがビルド・パブリッシュ済み (`publish/host/` と `publish/cli/` にバイナリが存在)

## テストの流れ

1. **cleanup.ps1**: 既存のProcTail HostプロセスとETWセッションを停止
2. **start-host.ps1**: ProcTail Hostを管理者権限で起動
3. **start-notepad.ps1**: テスト用のnotepadプロセスを起動
4. **start-cli.ps1**: ProcTail CLIを起動してnotepadプロセスを監視対象に追加
5. **file-save-test.ps1**: notepadでファイル保存操作を実行し、イベントを取得・確認

## 使用方法

### 統合テストの実行

```powershell
# WSL環境から実行
powershell.exe -ExecutionPolicy Bypass -File scripts/windows-test/run-test.ps1
```

### 個別スクリプトの実行

```powershell
# クリーンアップ
.\cleanup.ps1

# Host起動
.\start-host.ps1

# Notepad起動
$pid = .\start-notepad.ps1

# CLI起動とnotepad購読
.\start-cli.ps1 -NotepadPid $pid -Tag "my-test"

# ファイル保存テスト
.\file-save-test.ps1 -Tag "my-test"
```

## パラメータ

### run-test.ps1
- `-Tag`: 監視タグ名 (デフォルト: "test-notepad")
- `-SkipCleanup`: 初期クリーンアップをスキップ
- `-KeepProcesses`: テスト完了後もプロセスを維持

### start-cli.ps1
- `-NotepadPid`: 監視対象のnotepad PID (必須)
- `-CliPath`: ProcTail CLIの実行ファイルパス
- `-Tag`: 監視タグ名

### file-save-test.ps1
- `-CliPath`: ProcTail CLIの実行ファイルパス
- `-Tag`: イベント取得用のタグ名
- `-TestFile`: 保存するテストファイルのパス

## 注意事項

- 必ず管理者権限でPowerShellを実行してください
- テスト中はnotepadウィンドウをアクティブな状態に保ってください
- file-save-test.ps1実行時は、キーストロークが送信されるため他の作業は避けてください

## トラブルシューティング

- **UACプロンプトが表示される**: Hostの起動時に管理者権限が必要です。許可してください
- **notepadが見つからない**: notepad.exeがPATHに含まれていることを確認してください
- **イベントが取得できない**: ETWセッションが正常に開始されているか、またHostが正常に動作しているかを確認してください