#!/bin/bash

# Windows Integration Test Runner
# WSL環境からWindows統合テストを実行するスクリプト

set -e

echo "================================================"
echo "   ProcTail Windows Integration Test Runner"
echo "================================================"

# プロジェクトルートを取得
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
WINDOWS_TEST_DIR="C:/Temp/ProcTailTest"
WINDOWS_SCRIPTS_DIR="C:/Temp/ProcTailScripts"

echo "📁 Project root: $PROJECT_ROOT"
echo "📁 Windows test directory: $WINDOWS_TEST_DIR"

# Step 1: プロジェクトのビルド
echo
echo "🔨 Step 1: Building project..."
cd "$PROJECT_ROOT"

echo "Building in Release configuration..."
dotnet build --configuration Release

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo "✅ Build completed successfully"

# Step 2: Windows環境にファイルをコピー
echo
echo "📋 Step 2: Copying files to Windows environment..."

# Windows側のディレクトリを作成
powershell.exe -Command "
    if (Test-Path '$WINDOWS_TEST_DIR') { Remove-Item -Recurse -Force '$WINDOWS_TEST_DIR' }
    if (Test-Path '$WINDOWS_SCRIPTS_DIR') { Remove-Item -Recurse -Force '$WINDOWS_SCRIPTS_DIR' }
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/host' -Force | Out-Null
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/cli' -Force | Out-Null
    New-Item -ItemType Directory -Path '$WINDOWS_SCRIPTS_DIR' -Force | Out-Null
"

# ビルド成果物をコピー
echo "Copying Host binaries..."
powershell.exe -Command "
    Copy-Item -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\src\\ProcTail.Host\\bin\\Release\\net8.0\\*' -Destination '$WINDOWS_TEST_DIR/host' -Recurse -Force
"

echo "Copying CLI binaries..."
powershell.exe -Command "
    Copy-Item -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\src\\ProcTail.Cli\\bin\\Release\\net8.0\\*' -Destination '$WINDOWS_TEST_DIR/cli' -Recurse -Force
"

# PowerShellスクリプトをコピー
echo "Copying test scripts..."
powershell.exe -Command "
    Copy-Item -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\scripts\\windows-test\\*.ps1' -Destination '$WINDOWS_SCRIPTS_DIR' -Force
"

# appsettings.jsonのPipeName設定を修正
echo "Configuring appsettings.json..."
powershell.exe -Command "
    \$configPath = '$WINDOWS_TEST_DIR/host/appsettings.json'
    \$config = Get-Content \$configPath -Raw | ConvertFrom-Json
    \$config.NamedPipe.PipeName = 'ProcTail'
    \$config | ConvertTo-Json -Depth 10 | Set-Content \$configPath
"

echo "✅ Files copied and configured successfully"

# Step 3: PowerShellテストスクリプトを実行
echo
echo "🧪 Step 3: Running Windows integration test..."
echo "A PowerShell window will open as Administrator..."
echo "Please approve the UAC prompt when it appears."

# 管理者権限でPowerShellテストスクリプトを実行
echo "Starting PowerShell as Administrator..."
echo "Please approve the UAC prompt when it appears."
echo ""

# PowerShellウィンドウを開いたままにするため、-NoExit を追加
powershell.exe -Command "
    Start-Process PowerShell -ArgumentList '-NoExit -ExecutionPolicy RemoteSigned -Command \"try { & $WINDOWS_SCRIPTS_DIR\\run-test-final-fixed.ps1 } catch { Write-Host \\"Error: \$_\\" -ForegroundColor Red; Read-Host \\"Press Enter to exit\\" }\"' -Verb RunAs
"

echo ""
echo "🎉 Test execution initiated!"
echo "Check the PowerShell window for test results."
echo ""
echo "If the Host process fails to start, you can run diagnostics with:"
echo "  Start PowerShell as Administrator"
echo "  Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Force"
echo "  & '$WINDOWS_SCRIPTS_DIR\\diagnose-host-startup.ps1'"
echo ""
echo "Test files are located at: $WINDOWS_TEST_DIR"