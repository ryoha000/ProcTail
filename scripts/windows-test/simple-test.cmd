@echo off
echo === Windows環境でのdotnet test実行 ===

REM 一時ディレクトリ作成
set TEMP_DIR=C:\Temp\ProcTailTestSimple
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"
mkdir "%TEMP_DIR%"

echo ソースファイルをコピー中...
robocopy "\\wsl.localhost\Ubuntu\home\ryoha\workspace\proctail" "%TEMP_DIR%" /E /XD bin obj .git /XF *.user *.tmp /NFL /NDL /NJH /NJS /NC /NS /NP

echo ディレクトリ移動...
cd /d "%TEMP_DIR%"

echo ビルド実行中...
dotnet build --configuration Release
if %errorlevel% neq 0 (
    echo ビルド失敗
    pause
    exit /b %errorlevel%
)

echo テスト実行中...
dotnet test tests/ProcTail.System.Tests/ProcTail.System.Tests.csproj --configuration Release --logger console;verbosity=normal --filter "Category=Platform"

echo テスト完了
pause