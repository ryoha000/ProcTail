# ProcTail Windows Test Scripts

ProcTailのWindows環境統合テスト用PowerShellスクリプト群です。

## 前提条件

- Windows管理者権限
- PowerShell実行ポリシー設定 (RemoteSigned)
- .NET 8 ランタイム

## スクリプト一覧

### `run-test-final.ps1` 【メインテストスクリプト】
完全な統合テストを実行します。

**機能:**
- ETWセッション自動クリーンアップ
- Host起動とNotepad監視
- リアルタイムイベント検証
- 自動クリーンアップ

**実行方法:**
```powershell
# WSLから実行（管理者PowerShellが自動で開きます）
powershell.exe -Command "Start-Process PowerShell -ArgumentList '-ExecutionPolicy RemoteSigned -Command \"& \\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\scripts\\windows-test\\run-test-final.ps1; Read-Host Press Enter to exit\"' -Verb RunAs"
```

### `cleanup-etw-complete.ps1` 【クリーンアップユーティリティ】
ETWセッションを完全にクリーンアップします。

**実行方法:**
```powershell
powershell.exe -Command "Start-Process PowerShell -ArgumentList '-ExecutionPolicy RemoteSigned -Command \"& \\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\scripts\\windows-test\\cleanup-etw-complete.ps1; Read-Host Press Enter to exit\"' -Verb RunAs"
```

## テスト手順

1. **管理者PowerShell起動** (上記コマンドで自動実行)
2. **UACプロンプト承認** (「はい」をクリック)
3. **テスト自動実行**:
   - ETWクリーンアップ
   - ファイルコピー（WSL → Windows）
   - Host起動
   - Notepad起動・監視開始
4. **ファイル保存操作** (画面指示に従ってNotepadでファイル保存)
5. **結果確認** (イベント取得・表示)
6. **自動クリーンアップ**

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