# 開発者向けドキュメント

ProcTailの開発・拡張を行う方向けのドキュメントです。

## 📋 ドキュメント一覧

### 💻 [Developer-Guide.md](Developer-Guide.md)
**C#初心者向け開発ガイド**

- 開発環境のセットアップ（Visual Studio、.NET SDK）
- C#基礎知識とProcTailでの実践
- プロジェクト構造の理解
- 実際の開発作業パターン
- テストとデバッグの方法

**対象**: C#や.NET開発が初めての方、新規参加開発者

### 🏗️ [Architecture.md](Architecture.md)
**システム設計とアーキテクチャ**

- Clean Architectureの実装
- レイヤー構成とコンポーネント設計
- データフローと設計原則
- 技術選択の理由と将来の拡張性
- パフォーマンス考慮事項

**対象**: アーキテクト、上級開発者、システム設計を理解したい方

## 🎯 開発者向け学習パス

### 新規開発者向け
1. **Developer-Guide** で開発環境をセットアップ
2. プロジェクト構造を理解
3. 簡単な機能追加で実践
4. **Architecture** でシステム全体を理解

### 既存開発者向け
1. **Architecture** でシステム設計を確認
2. **Developer-Guide** の実践パターンを参照
3. 新機能開発またはリファクタリング

## 🚀 開発を始める

### 1. 環境セットアップ
```bash
# リポジトリクローン
git clone https://github.com/your-org/proctail.git
cd proctail

# 依存関係復元とビルド
dotnet restore
dotnet build

# テスト実行
dotnet test
```

### 2. 開発の流れ
```bash
# 機能ブランチ作成
git checkout -b feature/new-feature

# 開発とテスト
dotnet build
dotnet test

# コミットとプッシュ
git add .
git commit -m "feat: add new feature"
git push origin feature/new-feature
```

## 🔧 開発タスク別ガイド

### 新しいコマンドの追加
→ [Developer-Guide#新しいコマンドの追加](Developer-Guide.md#新しいコマンドの追加)

### 新しいイベントタイプの実装
→ [Developer-Guide#新しいイベントタイプの追加](Developer-Guide.md#新しいイベントタイプの追加)

### テストの書き方
→ [Developer-Guide#テストの書き方](Developer-Guide.md#テストの書き方)

### デバッグ方法
→ [Developer-Guide#デバッグ方法](Developer-Guide.md#デバッグ方法)

## 🏛️ アーキテクチャ概要

```
┌─────────────────┐    IPC        ┌──────────────────┐
│   CLI Client    │◄─────────────►│  Windows Service │
│ (Presentation)  │  Named Pipes  │   (Host)         │
└─────────────────┘               └──────────────────┘
                                           │
                                           │ ETW Events
                                           ▼
                                  ┌─────────────────┐
                                  │ Windows Kernel  │
                                  │ (ETW Providers) │
                                  └─────────────────┘
```

### 主要レイヤー
- **Core** - ドメインモデルとインターフェース
- **Application** - ビジネスロジック
- **Infrastructure** - Windows API実装
- **Presentation** - CLI/Service UI

詳細は **[Architecture.md](Architecture.md)** を参照

## 🧪 テスト戦略

### テストカテゴリ
- **Unit** - 全プラットフォーム実行可能
- **Integration** - モック使用の統合テスト
- **System** - Windows環境必須
- **System+RequiresWindowsAndAdministrator** - Windows+管理者権限必須

### テスト実行
```bash
# 全テスト
dotnet test

# プラットフォーム固有テスト
dotnet test --filter TestCategory=Platform

# Windows固有テスト（管理者権限で実行）
dotnet test --filter TestCategory=System
```

## 💡 開発のベストプラクティス

### コーディング規約
- 日本語コメント推奨
- インターフェースへの依存
- 非同期プログラミングの活用
- 適切なエラーハンドリング

### Git ワークフロー
- feature ブランチでの開発
- 詳細なコミットメッセージ
- プルリクエスト前のテスト実行

### パフォーマンス
- メモリリークの防止
- 非ブロッキング処理
- リソースの適切な解放

## 🔍 よくある開発タスク

### Visual Studio でのデバッグ
1. `ProcTail.Host` をスタートアッププロジェクトに設定
2. ブレークポイントを設定
3. F5でデバッグ開始

### 新機能のテスト
1. 単体テストを作成
2. 統合テストで動作確認
3. Windows環境でシステムテスト

## 📞 開発サポート

- **[GitHub Issues](https://github.com/your-org/proctail/issues)** - バグ報告・機能要求
- **[GitHub Discussions](https://github.com/your-org/proctail/discussions)** - 技術討論
- **[Contributing Guidelines](../../CONTRIBUTING.md)** - コントリビューション方法

---

**🎉 ProcTailの開発にようこそ！質問があれば遠慮なくIssueやDiscussionで聞いてください。**