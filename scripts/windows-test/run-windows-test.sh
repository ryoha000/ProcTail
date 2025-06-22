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

echo "Building test-process.exe for Windows..."
cd "$PROJECT_ROOT/tools/test-process"
GOOS=windows GOARCH=amd64 go build -o test-process.exe

if [ $? -ne 0 ]; then
    echo "❌ test-process.exe build failed"
    exit 1
fi

cd "$PROJECT_ROOT"
echo "✅ Build completed successfully"

# Step 2: ETW cleanup before file operations
echo
echo "🧹 Step 2: ETW cleanup to prevent file locks..."

# First copy cleanup script to Windows and run it
powershell.exe -Command "
    if (-not (Test-Path '$WINDOWS_SCRIPTS_DIR')) { 
        New-Item -ItemType Directory -Path '$WINDOWS_SCRIPTS_DIR' -Force | Out-Null 
    }
    Copy-Item -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\scripts\\windows-test\\cleanup-etw.ps1' -Destination '$WINDOWS_SCRIPTS_DIR\\cleanup-etw.ps1' -Force
"

echo "Running ETW cleanup to stop any existing Host processes..."
echo "This will request administrator privileges..."
powershell.exe -Command "
    try {
        # Run cleanup with administrator privileges
        Start-Process PowerShell -ArgumentList '-ExecutionPolicy RemoteSigned -Command \"& $WINDOWS_SCRIPTS_DIR\\cleanup-etw.ps1 -Silent; Start-Sleep 3\"' -Verb RunAs -Wait
        Write-Host 'ETW cleanup completed' -ForegroundColor Green
    }
    catch {
        Write-Host 'ETW cleanup failed, some files may be locked during copy' -ForegroundColor Yellow
    }
"

# Step 3: Windows環境にファイルをコピー
echo
echo "📋 Step 3: Copying files to Windows environment..."

# Windows側のディレクトリを作成
powershell.exe -Command "
    if (Test-Path '$WINDOWS_TEST_DIR') { Remove-Item -Recurse -Force '$WINDOWS_TEST_DIR' }
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/host' -Force | Out-Null
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/cli' -Force | Out-Null
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/tools' -Force | Out-Null
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

echo "Copying test-process.exe..."
powershell.exe -Command "
    Copy-Item -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\tools\\test-process\\test-process.exe' -Destination '$WINDOWS_TEST_DIR/tools/test-process.exe' -Force
"

# PowerShellスクリプトをコピー
echo "Copying test scripts..."
powershell.exe -Command "
    # BOMなしUTF-8で保存し直す
    \$scripts = Get-ChildItem -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\scripts\\windows-test\\*.ps1'
    foreach (\$script in \$scripts) {
        \$content = Get-Content -Path \$script.FullName -Raw
        \$outputPath = Join-Path '$WINDOWS_SCRIPTS_DIR' \$script.Name
        [System.IO.File]::WriteAllText(\$outputPath, \$content, [System.Text.UTF8Encoding]::new(\$false))
    }
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

# Step 4: PowerShellテストスクリプトを実行
echo
echo "🧪 Step 4: Running automated Windows integration test..."
echo "A PowerShell window will open as Administrator..."
echo "The test will run automatically using test-process.exe"
echo "Please approve the UAC prompt when it appears."

# 管理者権限でPowerShellテストスクリプトを実行
echo "Starting PowerShell as Administrator..."
echo "Please approve the UAC prompt when it appears."
echo ""

# PowerShellウィンドウを開いたままにするため、-NoExit を追加
# 単純なコマンドラインを使用してエスケープの問題を回避
powershell.exe -Command "Start-Process PowerShell -Verb RunAs -ArgumentList '-NoExit', '-ExecutionPolicy', 'RemoteSigned', '-File', '\"$WINDOWS_SCRIPTS_DIR\\integration-test.ps1\"'"

echo ""
echo "🎉 Automated test execution initiated!"
echo "Check the PowerShell window for test results."
echo "The test will run automatically without manual intervention."
echo ""
echo "If the Host process fails to start, you can run diagnostics with:"
echo "  Start PowerShell as Administrator"
echo "  Set-ExecutionPolicy RemoteSigned -Scope CurrentUser -Force"
echo "  & '$WINDOWS_SCRIPTS_DIR\\diagnose-host-startup.ps1'"
echo ""
echo "Test files are located at: $WINDOWS_TEST_DIR"
echo "test-process.exe is located at: $WINDOWS_TEST_DIR/tools/test-process.exe"