# ユーザー向けドキュメント

ProcTail CLIツールの使用方法について説明します。

## 📋 ドキュメント一覧

### 🚀 [Quick-Start.md](Quick-Start.md)
**5分でできるセットアップガイド**

- 初回インストールと設定
- 基本的な監視開始手順
- よくある使用パターン
- トラブルシューティング

**対象**: ProcTailを初めて使う方

### 📖 [CLI-Usage.md](CLI-Usage.md)
**詳細な使用方法ガイド**

- 全機能の詳細説明
- 実践的な使用例
- 管理者権限とUAC
- 設定ファイルのカスタマイズ

**対象**: ProcTailを日常的に使用する方

### 📚 [CLI-Reference.md](CLI-Reference.md)
**完全コマンドリファレンス**

- 全コマンドとオプション
- パラメータ詳細
- 出力形式
- エラーコード一覧

**対象**: 詳細な仕様を知りたい方、スクリプト作成者

## 🎯 学習パス

### 初心者向け学習順序
1. **Quick-Start** → 基本操作を習得
2. **CLI-Usage** → 詳細機能を学習
3. **CLI-Reference** → 必要時に参照

### 用途別クイックアクセス

#### 🔍 監視開始方法
```bash
# プロセス監視を開始
proctail service start
proctail add --name "notepad.exe" --tag "demo"
```
→ [Quick-Start#監視開始](Quick-Start.md#監視開始) で詳細確認

#### 📊 結果確認方法
```bash
# イベント確認
proctail events --tag "demo"
proctail status
```
→ [CLI-Usage#イベント取得](CLI-Usage.md#イベント取得) で詳細確認

#### 🛠️ 高度な使用方法
```bash
# JSON出力でデータ分析
proctail events --tag "security" --format json > events.json
```
→ [CLI-Reference#出力形式](CLI-Reference.md#出力形式) で詳細確認

## 💡 ヒント

- **管理者権限**: ETW監視機能は管理者権限が必要です
- **UAC対応**: 権限不足時は自動でUACプロンプトが表示されます
- **出力形式**: Table、JSON、CSV形式で結果を取得できます
- **リアルタイム監視**: `--follow` オプションでtail -f的な監視が可能です

## 🆘 サポート

問題が発生した場合は以下を確認してください：

1. **[Quick-Start トラブルシューティング](Quick-Start.md#トラブルシューティング)**
2. **[CLI-Usage トラブルシューティング](CLI-Usage.md#トラブルシューティング)**
3. **[GitHub Issues](https://github.com/your-org/proctail/issues)** で既存の問題を検索
4. 新しい問題の報告は Issue を作成