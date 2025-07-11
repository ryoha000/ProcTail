# ProcTail

ProcTailは、Windows環境でETW (Event Tracing for Windows) を使用してプロセスのファイル操作と子プロセス作成をリアルタイム監視するツールです。

## ✨ 特徴

- 🔍 **リアルタイム監視**: ETWを使用したプロセス・ファイル操作の即座な検出
- 🏷️ **タグベース管理**: プロセスをタグで分類して効率的に管理
- 🔧 **CLI & サービス**: コマンドラインツールとWindowsサービスの両方で利用可能
- 🛡️ **管理者権限対応**: 必要に応じて自動でUACプロンプトを表示
- 📊 **多様な出力形式**: Table、JSON、CSV形式での結果出力
- 🔄 **Named Pipes IPC**: プロセス間通信による制御・情報取得

## 🚀 クイックスタート

### インストール
1. 最新リリースをダウンロード
2. 管理者権限でコマンドプロンプトを開く
3. ProcTailサービスをインストール・開始

```bash
# サービスのインストールと開始
proctail service install
proctail service start

# 監視対象を追加
proctail add --name "notepad.exe" --tag "demo"

# メモ帳を起動してファイル操作を実行
notepad.exe

# イベントを確認
proctail events --tag "demo"
```

## 📖 ドキュメント

### 👤 ユーザー向け
- **[クイックスタートガイド](docs/user/Quick-Start.md)** - 5分で始める
- **[CLI使用方法](docs/user/CLI-Usage.md)** - 詳細な使い方ガイド  
- **[コマンドリファレンス](docs/user/CLI-Reference.md)** - 全コマンド・オプション一覧

### 👨‍💻 開発者向け
- **[開発者ガイド](docs/developer/Developer-Guide.md)** - C#初心者向け開発ガイド
- **[アーキテクチャ](docs/developer/Architecture.md)** - システム設計と技術選択

### 🔌 API仕様
- **[API リファレンス](docs/api/API-Reference.md)** - IPC通信とデータモデル

## 💻 CLI使用例

### 基本的な監視
```bash
# プロセスIDで監視
proctail add --pid 1234 --tag "my-app"

# プロセス名で監視
proctail add --name "chrome.exe" --tag "browser"

# リアルタイム監視
proctail events --tag "browser" --follow
```

### 開発・デバッグ用途
```bash
# Visual Studio関連プロセスを監視
proctail add --name "devenv.exe" --tag "development"
proctail add --name "MSBuild.exe" --tag "development"

# ビルド中のファイル操作を確認
proctail events --tag "development" --count 100
```

### セキュリティ監査
```bash
# システムプロセスを監視
proctail add --name "winlogon.exe" --tag "security"
proctail add --name "lsass.exe" --tag "security"

# CSVでエクスポートして分析
proctail events --tag "security" --format csv > audit.csv
```

## 🏗️ アーキテクチャ

```
┌─────────────────┐    Named Pipes     ┌──────────────────┐
│   CLI Client    │◄───────────────────┤  ProcTail Host   │
│   proctail.exe  │                    │  (Windows Service) │
└─────────────────┘                    └──────────────────┘
                                                │
                                                │ ETW Events
                                                ▼
                                       ┌─────────────────┐
                                       │ Windows Kernel  │
                                       │ (ETW Providers) │
                                       └─────────────────┘
```

### 主要コンポーネント

- **ProcTail.Host**: ETW監視とNamed Pipesサーバーを実行するWindowsサービス
- **ProcTail.Cli**: ユーザーが操作するコマンドラインインターface
- **ProcTail.Core**: 共通のインターフェースとモデル定義
- **ProcTail.Infrastructure**: ETWとNamed PipesのWindows API実装
- **ProcTail.Application**: ビジネスロジックとサービス層

## 🛡️ セキュリティと権限

### 必要な権限
- **ETW監視**: Windows管理者権限が必要
- **Named Pipes**: ローカルユーザーのアクセス許可
- **サービス管理**: 管理者権限が必要

### UAC対応
権限が不足している場合、自動的にUACプロンプトが表示されます：

```bash
# 管理者権限が必要な場合、UACが自動表示
proctail service start

# UACを無効にする場合
proctail service start --no-uac
```

## 📊 監視対象イベント

| イベント種類 | 説明 |
|-------------|------|
| **ファイル操作** | Create, Write, Delete, Rename, SetInfo |
| **プロセス操作** | Process Start, Process End |
| **子プロセス** | 親プロセスと同じタグで自動監視 |

*注意: ファイルRead操作は高頻度のため除外されています*

## ⚙️ 設定

### 設定ファイル例 (appsettings.json)
```json
{
  "ProcTail": {
    "MaxEventsPerTag": 10000,
    "EventRetentionDays": 30,
    "DataDirectory": "C:\\ProcTail\\Data"
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

## 🧪 システム要件

- **OS**: Windows 10/11 または Windows Server 2019/2022
- **Framework**: .NET 8.0 Runtime
- **権限**: 管理者権限（ETW監視使用時）
- **アーキテクチャ**: x64

## 🔧 開発・ビルド

### 前提条件
- .NET 8.0 SDK
- Visual Studio 2022 または VS Code
- Windows 開発環境

### ビルド
```bash
# 全体をビルド
dotnet build

# CLIツールをビルド
dotnet build src/ProcTail.Cli/

# テスト実行
dotnet test
```

### 開発用の実行
```bash
# サービスをデバッグモードで起動
dotnet run --project src/ProcTail.Host/

# CLIを実行
dotnet run --project src/ProcTail.Cli/ -- add --name "notepad.exe" --tag "debug"
```

## 🧪 テスト

### テストカテゴリ
- **Unit**: 全プラットフォームで実行可能
- **Integration**: モックを使用した統合テスト
- **System**: Windows環境必須
- **System+RequiresWindowsAndAdministrator**: Windows+管理者権限必須

### テスト実行
```bash
# 全テスト実行
dotnet test
```

## 🚀 リリース手順

### 自動リリース（推奨）

タグを作成してプッシュするだけで、GitHub Actionsが自動的にリリースを作成します：

```bash
# 新しいバージョンのタグを作成
git tag v1.0.0

# タグをリモートにプッシュ（自動リリース開始）
git push origin v1.0.0
```

### 手動リリース

GitHub ActionsのWorkflowsタブから"Release"ワークフローを手動実行することも可能です：

1. GitHubリポジトリの **Actions** タブを開く
2. **Release** ワークフローを選択
3. **Run workflow** をクリック
4. タグ名（例: v1.0.1）を入力して実行

### リリース内容

自動リリースには以下が含まれます：

#### 📦 リリースアセット
- **ProcTail-{version}-self-contained-win-x64.zip** - .NET Runtime不要版（推奨）
- **ProcTail-{version}-framework-dependent-win-x64.zip** - .NET 8 Runtime必要版
- **checksums.txt** - SHA256チェックサム

#### 📋 各パッケージ内容
- `host/` - ProcTailサービス実行ファイル
- `cli/` - CLIツール実行ファイル
- `install.cmd` - サービスインストールスクリプト
- `uninstall.cmd` - サービスアンインストールスクリプト
- `README.md` - 使用方法

#### 📝 自動生成される内容
- 前回リリースからの変更履歴
- インストール手順
- システム要件
- ファイルサイズとチェックサム情報

### バージョニング規則

セマンティックバージョニング（SemVer）に従ってください：

- **MAJOR** (v2.0.0): 破壊的変更
- **MINOR** (v1.1.0): 新機能追加（後方互換）
- **PATCH** (v1.0.1): バグ修正

```bash
# パッチリリース例
git tag v1.0.1
git push origin v1.0.1

# マイナーリリース例  
git tag v1.1.0
git push origin v1.1.0

# メジャーリリース例
git tag v2.0.0
git push origin v2.0.0
```

### プレリリース

アルファ・ベータ版には `-alpha` や `-beta` サフィックスを付けてください：

```bash
# プレリリース例
git tag v1.1.0-beta.1
git push origin v1.1.0-beta.1
```

プレリリースは自動的に「Pre-release」としてマークされます。
