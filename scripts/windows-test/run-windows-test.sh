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

echo "Publishing in Release configuration with self-contained runtime..."
dotnet publish src/ProcTail.Host/ProcTail.Host.csproj --configuration Release --runtime win-x64 --self-contained true --output "$PROJECT_ROOT/publish/host"
dotnet publish src/ProcTail.Cli/ProcTail.Cli.csproj --configuration Release --runtime win-x64 --self-contained true --output "$PROJECT_ROOT/publish/cli"

if [ $? -ne 0 ]; then
    echo "❌ Publish failed"
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
pwsh.exe -Command "
    if (-not (Test-Path '$WINDOWS_SCRIPTS_DIR')) { 
        New-Item -ItemType Directory -Path '$WINDOWS_SCRIPTS_DIR' -Force | Out-Null 
    }
    Copy-Item -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\scripts\\windows-test\\cleanup-etw.ps1' -Destination '$WINDOWS_SCRIPTS_DIR\\cleanup-etw.ps1' -Force
"

echo "Running ETW cleanup to stop any existing Host processes..."
echo "This will request administrator privileges..."
pwsh.exe -Command "
    try {
        # Run cleanup with administrator privileges
        Start-Process PWSH -ArgumentList '-ExecutionPolicy RemoteSigned -Command \"& $WINDOWS_SCRIPTS_DIR\\cleanup-etw.ps1 -Silent; Start-Sleep 3\"' -Verb RunAs -Wait
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
pwsh.exe -Command "
    if (Test-Path '$WINDOWS_TEST_DIR') { Remove-Item -Recurse -Force '$WINDOWS_TEST_DIR' }
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/host' -Force | Out-Null
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/cli' -Force | Out-Null
    New-Item -ItemType Directory -Path '$WINDOWS_TEST_DIR/tools' -Force | Out-Null
"

# ビルド成果物をコピー（Windows用ファイルのみ）
echo "Copying Host binaries..."
pwsh.exe -Command "
    \$hostFiles = @('ProcTail.Host.exe', 'ProcTail.Host.dll', 'ProcTail.Host.runtimeconfig.json', 'appsettings.json', 'app.manifest', '*.dll')
    foreach (\$pattern in \$hostFiles) {
        \$files = Get-ChildItem -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\publish\\host' -Name \$pattern -ErrorAction SilentlyContinue
        foreach (\$file in \$files) {
            Copy-Item -Path \"\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\publish\\host\\\$file\" -Destination '$WINDOWS_TEST_DIR/host' -Force
        }
    }
"

echo "Copying CLI binaries..."
pwsh.exe -Command "
    \$cliFiles = @('*.dll', '*.pdb', '*.xml', '*.exe')
    foreach (\$pattern in \$cliFiles) {
        \$files = Get-ChildItem -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\publish\\cli' -Name \$pattern -ErrorAction SilentlyContinue
        foreach (\$file in \$files) {
            Copy-Item -Path \"\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\publish\\cli\\\$file\" -Destination '$WINDOWS_TEST_DIR/cli' -Force
        }
    }
"

echo "Copying test-process.exe..."
pwsh.exe -Command "
    Copy-Item -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\tools\\test-process\\test-process.exe' -Destination '$WINDOWS_TEST_DIR/tools/test-process.exe' -Force
"

# PowerShellスクリプトをコピー
echo "Copying test scripts..."
pwsh.exe -Command "
    # BOM付きUTF-8で保存し直す
    \$scripts = Get-ChildItem -Path '\\\\wsl.localhost\\Ubuntu\\home\\ryoha\\workspace\\proctail\\scripts\\windows-test\\*.ps1'
    foreach (\$script in \$scripts) {
        \$content = Get-Content -Path \$script.FullName -Raw -Encoding UTF8
        \$outputPath = Join-Path '$WINDOWS_SCRIPTS_DIR' \$script.Name
        
        # BOM付きUTF-8エンコーディングで保存
        \$utf8WithBom = New-Object System.Text.UTF8Encoding(\$true)
        [System.IO.File]::WriteAllText(\$outputPath, \$content, \$utf8WithBom)
    }
"

# appsettings.jsonのPipeName設定を修正
echo "Configuring appsettings.json..."
pwsh.exe -Command "
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
pwsh.exe -Command "Start-Process PWSH -Verb RunAs -ArgumentList '-NoExit', '-ExecutionPolicy', 'RemoteSigned', '-File', '\"$WINDOWS_SCRIPTS_DIR\\integration-test.ps1\"' -Wait -WorkingDirectory '$WINDOWS_TEST_DIR'"

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