# ProcTail Windows Test Scripts

ProcTailのWindows環境統合テスト用PowerShellスクリプト群です。

## 前提条件

- Windows管理者権限
- PowerShell実行ポリシー設定 (RemoteSigned)
- .NET 8 ランタイム

## スクリプト一覧

### `run-windows-test.sh` 【メインランナー】
ビルドからテスト実行まで全体を制御するUnix shell script。

**機能:**
- プロジェクトの自動ビルド
- Windows環境へのファイルコピー
- 設定ファイルの自動調整
- PowerShellテストスクリプトの起動

**実行方法:**
```bash
# WSLから実行
./scripts/windows-test/run-windows-test.sh
```

### `run-test-final.ps1` 【テスト実行】
Windows環境での統合テスト実行を担当。

**機能:**
- ETWセッションクリーンアップ
- Host起動とNotepad監視
- リアルタイムイベント検証
- 自動クリーンアップ

**前提条件:** 事前に `run-windows-test.sh` でファイル準備が必要

### `cleanup-etw-complete.ps1` 【クリーンアップユーティリティ】
ETWセッションを完全にクリーンアップします。

## テスト手順

1. **Shell scriptでビルド・準備**:
   ```bash
   ./scripts/windows-test/run-windows-test.sh
   ```
   - プロジェクトビルド
   - Windows環境へファイルコピー
   - 設定ファイル調整

2. **PowerShellテスト実行** (自動で管理者権限ウィンドウが開く):
   - UACプロンプト承認 (「はい」をクリック)
   - ETWクリーンアップ
   - Host起動
   - Notepad起動・監視開始

3. **ファイル保存操作** (画面指示に従ってNotepadでファイル保存)

4. **結果確認** (イベント取得・表示)

5. **自動クリーンアップ**

## 成功時の出力例

```
===============================================
   ProcTail Final Integration Test
===============================================
✓ Administrator privileges confirmed
✓ ETW sessions cleaned up  
✓ Files copied to C:\Temp\ProcTailTest
✓ Host is running (PID: 1234)
✓ Notepad started and monitored (PID: 5678)
→ Please save a file in Notepad (Ctrl+S)...
✓ Events detected: FileIO/Create, FileIO/Write
✓ Test completed successfully
```

## トラブルシューティング

### よくある問題

**「管理者権限が必要」**
→ PowerShellを右クリック → 「管理者として実行」

**「実行ポリシー制限」** 
→ `Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Force`

**「Named Pipe接続失敗」**
→ Hostサービス起動確認、appsettings.json設定確認

**「イベントが検出されない」**
→ 監視対象プロセス内でファイル操作実行、ETWセッション確認

## アーキテクチャ

- **Host Service**: ETW監視用バックグラウンドサービス
- **CLI Tool**: 監視制御用コマンドラインツール  
- **Named Pipes**: Host-CLI間IPC通信
- **ETW**: Windowsイベントトレーシング

スクリプトは以下を自動処理します:
- WSL-Windows間ファイルシステム連携
- ETWセッション管理
- プロセスライフサイクル管理
- リアルタイムイベント検証