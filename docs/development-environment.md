# 開発環境設定ガイド

## 1. 概要

ProcTailプロジェクトは、Windows固有のAPIを使用するためWindows環境でのテストが必要ですが、開発の大部分はWSL（Windows Subsystem for Linux）環境で効率的に行えるように設計されています。

## 2. 推奨開発環境

### 2.1 ハイブリッド開発アプローチ

```
┌─────────────────────────────────────┐
│           Windows Host              │
│  ┌─────────────────────────────┐    │
│  │         WSL 2 環境          │    │
│  │  - 日常的な開発作業         │    │
│  │  - ユニット・統合テスト     │    │
│  │  - ビルド・デバッグ         │    │
│  └─────────────────────────────┘    │
│  - Windows固有テスト                │
│  - ETW・Named Pipes テスト          │
│  - 管理者権限テスト                 │
└─────────────────────────────────────┘
```

### 2.2 必要なソフトウェア

#### Windows側
- **Windows 10 20H1 (Build 19041) 以降** または **Windows 11**
- **WSL 2** が有効
- **.NET 8 SDK** (Windows版)
- **PowerShell 5.1 以降** または **PowerShell Core 7+**
- **Visual Studio 2022** または **Visual Studio Code** (推奨)

#### WSL側
- **Ubuntu 20.04 LTS** または **Ubuntu 22.04 LTS** (推奨)
- **.NET 8 SDK** (Linux版)
- **Git**
- 任意の開発ツール（vim, nano, etc.）

## 3. 環境セットアップ手順

### 3.1 WSL 2 のインストールと設定

```powershell
# 管理者権限でPowerShellを起動

# WSL機能の有効化
dism.exe /online /enable-feature /featurename:Microsoft-Windows-Subsystem-Linux /all /norestart

# 仮想マシンプラットフォーム機能の有効化
dism.exe /online /enable-feature /featurename:VirtualMachinePlatform /all /norestart

# 再起動後、WSL 2をデフォルトに設定
wsl --set-default-version 2

# Ubuntu のインストール
wsl --install -d Ubuntu-22.04
```

### 3.2 .NET SDK のインストール

#### Windows側
1. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)をダウンロード・インストール
2. コマンドプロンプトで確認：
   ```cmd
   dotnet --version
   ```

#### WSL側
```bash
# Microsoft パッケージリポジトリの追加
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb

# .NET SDK のインストール
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

# 確認
dotnet --version
```

### 3.3 開発ツールのインストール

#### WSL側での追加ツール
```bash
# 基本的な開発ツール
sudo apt-get install -y git curl wget unzip

# ビルドツール
sudo apt-get install -y build-essential

# テストレポート生成ツール
dotnet tool install -g dotnet-reportgenerator-globaltool

# カバレッジ収集ツール
dotnet tool install -g dotnet-coverage
```

### 3.4 Visual Studio Code の設定（推奨）

#### 必要な拡張機能
```json
{
  "recommendations": [
    "ms-dotnettools.csharp",
    "ms-vscode-remote.remote-wsl",
    "ms-vscode.powershell",
    "ms-dotnettools.vscode-dotnet-runtime",
    "formulahendry.dotnet-test-explorer",
    "ryanluker.vscode-coverage-gutters"
  ]
}
```

#### WSL統合設定
1. VS CodeでWSL拡張機能をインストール
2. WSL内でプロジェクトを開く：
   ```bash
   code /path/to/proctail
   ```

## 4. プロジェクトクローンと初期設定

### 4.1 プロジェクトクローン

```bash
# WSL環境内で実行
git clone <repository-url> proctail
cd proctail

# スクリプトに実行権限付与
chmod +x scripts/*.sh
```

### 4.2 プロジェクト構造の確認

```bash
# プロジェクト構造表示
tree -I 'bin|obj|node_modules' -L 3
```

期待される構造：
```
proctail/
├── src/
├── tests/
├── docs/
├── scripts/
├── ProcTail.sln
└── README.md
```

## 5. 開発ワークフロー

### 5.1 日常的な開発（WSL環境）

```bash
# プロジェクトビルド
dotnet build

# ユニットテスト実行
./scripts/run-tests.sh unit

# 統合テスト実行
./scripts/run-tests.sh integration

# WSLで安全に実行可能な全テスト
./scripts/run-tests.sh all-safe --verbose
```

### 5.2 Windows固有機能のテスト

```bash
# WSLからWindows PowerShellを呼び出してWindows固有テスト実行
powershell.exe -File ./scripts/run-windows-tests.ps1 admin -ElevateIfNeeded

# 全テスト（WSL + Windows）の統合実行
./scripts/run-all-tests.sh --verbose --coverage
```

### 5.3 デバッグ環境

#### WSL でのデバッグ
```bash
# デバッグビルド
dotnet build --configuration Debug

# VS Codeでデバッグ実行
code .
# F5キーでデバッグ開始
```

#### Windows でのデバッグ（ETW使用時）
1. Visual Studioを**管理者権限**で起動
2. WSLのプロジェクトディレクトリをマウント
3. Windows固有のテストプロジェクトを開く

## 6. テスト実行戦略

### 6.1 開発フェーズ別テスト実行

| フェーズ | 実行環境 | テストコマンド | 実行頻度 |
|----------|----------|----------------|----------|
| 実装中 | WSL | `./scripts/run-tests.sh unit` | 毎回 |
| 機能完成 | WSL | `./scripts/run-tests.sh integration` | 機能単位 |
| 統合前 | WSL | `./scripts/run-tests.sh all-safe` | プルリク前 |
| 最終確認 | WSL+Windows | `./scripts/run-all-tests.sh` | リリース前 |

### 6.2 パフォーマンステスト

```bash
# パフォーマンステスト実行（Windows環境）
powershell.exe -File ./scripts/run-windows-tests.ps1 performance -Verbose

# 結果分析
# TestResults/Performance/ ディレクトリ内のレポートを確認
```

## 7. トラブルシューティング

### 7.1 よくある問題と解決策

#### 問題: WSLから.NET SDKが見つからない
```bash
# 解決策: PATHの確認と修正
echo $PATH
export PATH="$PATH:/usr/share/dotnet"
echo 'export PATH="$PATH:/usr/share/dotnet"' >> ~/.bashrc
```

#### 問題: PowerShell.exeにアクセスできない
```bash
# 解決策: WSLからWindows側のPowerShellパス確認
which powershell.exe
# もしくは
/mnt/c/Windows/System32/WindowsPowerShell/v1.0/powershell.exe -Version
```

#### 問題: 管理者権限テストが失敗する
```powershell
# 解決策: UACを適切に設定し、スクリプトに管理者権限を付与
# scripts/run-windows-tests.ps1 の -ElevateIfNeeded オプションを使用
```

#### 問題: ETWアクセス権限エラー
```powershell
# 解決策: Event Log Readersグループに現在のユーザーを追加
net localgroup "Event Log Readers" $env:USERNAME /add
# システム再起動が必要な場合があります
```

### 7.2 ログとトレース

#### 開発時のログ設定
```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "ProcTail": "Trace",
      "Microsoft": "Warning"
    },
    "Console": {
      "IncludeScopes": true,
      "TimestampFormat": "yyyy-MM-dd HH:mm:ss.fff "
    }
  }
}
```

#### ETWトレース収集
```powershell
# 開発時のETWセッション監視
wevtutil el | findstr ProcTail
# ログ確認
wevtutil qe Application /f:text /q:"*[System[Provider[@Name='ProcTail']]]"
```

## 8. CI/CD 統合

### 8.1 ローカルCI/CDシミュレーション

```bash
# CI/CDパイプラインのローカル実行シミュレーション
./scripts/simulate-ci.sh

# 具体的な実行内容：
# 1. コードフォーマット確認
# 2. ビルド
# 3. ユニットテスト
# 4. 統合テスト
# 5. セキュリティスキャン
# 6. パッケージング
```

### 8.2 プルリクエスト前チェックリスト

```bash
# 1. コードフォーマット
dotnet format --verify-no-changes

# 2. ビルド確認
dotnet build --configuration Release

# 3. 全テスト実行
./scripts/run-all-tests.sh --configuration Release

# 4. カバレッジ確認
./scripts/run-all-tests.sh --coverage
# カバレッジが80%以上であることを確認
```

## 9. 推奨 IDE 設定

### 9.1 Visual Studio Code 設定

#### settings.json (ワークスペース設定)
```json
{
  "dotnet.defaultSolution": "ProcTail.sln",
  "omnisharp.enableEditorConfigSupport": true,
  "editor.formatOnSave": true,
  "files.trimTrailingWhitespace": true,
  "terminal.integrated.defaultProfile.windows": "PowerShell",
  "remote.WSL.fileWatcher.polling": true,
  "dotnet-test-explorer.testProjectPath": "tests/**/*.csproj"
}
```

#### launch.json (デバッグ設定)
```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "name": "Launch ProcTail (WSL)",
      "type": "coreclr",
      "request": "launch",
      "program": "${workspaceFolder}/src/ProcTail.Host/bin/Debug/net8.0/ProcTail.Host.dll",
      "args": [],
      "cwd": "${workspaceFolder}",
      "env": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      },
      "sourceFileMap": {
        "/Views": "${workspaceFolder}/Views"
      }
    }
  ]
}
```

### 9.2 EditorConfig 設定

#### .editorconfig
```ini
root = true

[*]
charset = utf-8
end_of_line = crlf
trim_trailing_whitespace = true
insert_final_newline = true
indent_style = space
indent_size = 4

[*.{cs,csx,vb,vbx}]
indent_size = 4

[*.{json,js,yml,yaml}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false
```

この開発環境設定により、効率的でテスタブルなProcTail開発が可能になります。