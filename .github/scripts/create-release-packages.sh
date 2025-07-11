#!/bin/bash

set -e

VERSION="$1"
if [ -z "$VERSION" ]; then
    echo "Error: Version parameter is required"
    exit 1
fi

echo "Creating release packages for version: $VERSION"

# ディレクトリ作成
mkdir -p "./release-packages"

# Self-contained版のパッケージ作成
SELF_CONTAINED_DIR="./release-packages/ProcTail-$VERSION-self-contained"
mkdir -p "$SELF_CONTAINED_DIR/host"
mkdir -p "$SELF_CONTAINED_DIR/cli"

# Self-contained版ファイルコピー
cp -r ./publish/host-win-x64/* "$SELF_CONTAINED_DIR/host/"
cp -r ./publish/cli-win-x64/* "$SELF_CONTAINED_DIR/cli/"

# Framework-dependent版のパッケージ作成
FRAMEWORK_DIR="./release-packages/ProcTail-$VERSION-framework-dependent"
mkdir -p "$FRAMEWORK_DIR/host"
mkdir -p "$FRAMEWORK_DIR/cli"

# Framework-dependent版ファイルコピー
cp -r ./publish/host-framework-dependent/* "$FRAMEWORK_DIR/host/"
cp -r ./publish/cli-framework-dependent/* "$FRAMEWORK_DIR/cli/"

# READMEコピー
if [ -f "README.md" ]; then
    cp "README.md" "$SELF_CONTAINED_DIR/"
    cp "README.md" "$FRAMEWORK_DIR/"
fi

echo "Release packages created successfully"