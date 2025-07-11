#!/bin/bash

set -e

VERSION="$1"
if [ -z "$VERSION" ]; then
    echo "Error: Version parameter is required"
    exit 1
fi

echo "Generating release notes for version: $VERSION"

# 前回のタグを取得
PREVIOUS_TAG=$(git describe --tags --abbrev=0 HEAD~1 2>/dev/null || echo "")

cat > release_notes.md << EOF
# ProcTail $VERSION Release

EOF

if [ -n "$PREVIOUS_TAG" ]; then
    echo "## Changes since $PREVIOUS_TAG" >> release_notes.md
    echo "" >> release_notes.md
    
    # コミットログから変更履歴を生成
    git log --oneline --no-merges ${PREVIOUS_TAG}..HEAD | while read line; do
        echo "- $line" >> release_notes.md
    done
else
    echo "## Initial Release" >> release_notes.md
    echo "" >> release_notes.md
    echo "This is the initial release of ProcTail." >> release_notes.md
fi

cat >> release_notes.md << EOF

## Installation

### Self-Contained Version (推奨)
- .NET Runtimeが不要
- \`ProcTail-$VERSION-self-contained-win-x64.zip\` をダウンロード
- 展開後、管理者権限でサービスをインストール

### Framework-Dependent Version
- .NET 8 Runtime が必要
- \`ProcTail-$VERSION-framework-dependent-win-x64.zip\` をダウンロード
- 展開後、管理者権限でサービスをインストール

## Usage

サービス開始: \`sc start ProcTail\`
CLIツール: \`cli/ProcTail.Cli.exe --help\`

## Requirements

- Windows 10/11 or Windows Server 2016+
- 管理者権限 (ETW監視のため)
- .NET 8 Runtime (Framework-dependent版のみ)

## File Checksums

\`\`\`
EOF

# チェックサムファイルの内容を追加
if [ -f "./release-artifacts/checksums.txt" ]; then
    cat ./release-artifacts/checksums.txt >> release_notes.md
fi

echo '```' >> release_notes.md

# ファイルサイズ情報も追加
echo "" >> release_notes.md
echo "## File Sizes" >> release_notes.md
echo "" >> release_notes.md

for file in ./release-artifacts/*.zip; do
    if [ -f "$file" ]; then
        size=$(du -h "$file" | cut -f1)
        filename=$(basename "$file")
        echo "- $filename: $size" >> release_notes.md
    fi
done

echo "Release notes generated successfully"