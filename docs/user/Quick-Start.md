# ProcTail クイックスタートガイド

ProcTailを使い始めるための簡単なガイドです。5分でプロセス監視を開始できます。

## ⚡ 1分でできるセットアップ

### 1️⃣ 管理者権限でコマンドプロンプトを開く
```
Windows + X → "Windows PowerShell (管理者)" または "コマンドプロンプト (管理者)"
```

### 2️⃣ ProcTailサービスをインストール・開始
```bash
# サービスをインストール
proctail service install

# サービスを開始
proctail service start

# 状態確認
proctail status
```

### 3️⃣ 監視を開始
```bash
# メモ帳を監視対象に追加
proctail add --name "notepad.exe" --tag "demo"

# メモ帳を起動してファイルを保存
notepad.exe

# イベントを確認
proctail events --tag "demo"
```

## 🎯 よくある使用パターン

### パターン1: 特定のアプリケーションをデバッグ

```bash
# Visual Studioを監視
proctail add --name "devenv.exe" --tag "vs-debug"

# リアルタイムでイベントを表示
proctail events --tag "vs-debug" --follow
```

### パターン2: システム全体のセキュリティ監査

```bash
# 重要なシステムプロセスを監視
proctail add --name "winlogon.exe" --tag "security"
proctail add --name "lsass.exe" --tag "security"
proctail add --name "svchost.exe" --tag "security"

# 1時間後に結果を確認
proctail events --tag "security" --count 1000 --format csv > security_audit.csv
```

### パターン3: パフォーマンス分析

```bash
# データベースサーバーを監視
proctail add --name "sqlservr.exe" --tag "db-perf"

# 定期的に統計を確認
proctail status --format json
```

## 🛠️ 便利なコマンド組み合わせ

### すべての監視を一度にクリア
```bash
# 現在の監視対象を確認
proctail list

# 各タグのイベントをクリア
proctail clear --tag "demo" --yes
proctail clear --tag "vs-debug" --yes
```

### JSON出力でデータ分析
```bash
# PowerShellでの分析例
$events = proctail events --tag "security" --format json | ConvertFrom-Json
$events.events | Where-Object { $_.eventType -eq "FileOperation" } | Measure-Object
```

### バッチファイルでの自動化
```batch
@echo off
echo =================================
echo ProcTail 自動監視スクリプト
echo =================================

rem 監視開始
proctail add --name "chrome.exe" --tag "web-browser"
proctail add --name "msedge.exe" --tag "web-browser"

echo 監視を開始しました。60秒後に結果を表示します...
timeout /t 60

echo === 監視結果 ===
proctail events --tag "web-browser"

pause
```

## 🚨 トラブル時の確認事項

### サービスが開始しない場合
```bash
# 1. 権限を確認
whoami /groups | findstr "S-1-5-32-544"

# 2. サービス状態を確認  
sc query ProcTail

# 3. イベントログを確認
eventvwr.msc
```

### イベントが記録されない場合
```bash
# 1. ETW状態を確認
proctail status --format json

# 2. プロセスが実行中か確認
tasklist | findstr "notepad"

# 3. 監視対象リストを確認
proctail list
```

## 🎓 次のステップ

- [詳細なCLI使い方ガイド](CLI-Usage.md) を読む
- [コマンドリファレンス](CLI-Reference.md) で全オプションを確認
- 設定ファイルをカスタマイズして運用に適用

## 📞 サポート

問題が発生した場合は、以下の情報と共にお問い合わせください：

```bash
# システム情報を収集
proctail version
proctail status --format json
systeminfo | findstr /i "OS Name OS Version"
```

---

**🎉 これでProcTailの基本的な使い方をマスターしました！**