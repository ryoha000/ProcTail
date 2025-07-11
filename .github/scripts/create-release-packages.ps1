param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

Write-Host "Creating release packages for version: $Version"

# ディレクトリ作成
New-Item -ItemType Directory -Force -Path "./release-packages" | Out-Null

# Self-contained版のパッケージ作成
$selfContainedDir = "./release-packages/ProcTail-$Version-self-contained"
New-Item -ItemType Directory -Force -Path $selfContainedDir | Out-Null
New-Item -ItemType Directory -Force -Path "$selfContainedDir/host" | Out-Null
New-Item -ItemType Directory -Force -Path "$selfContainedDir/cli" | Out-Null

# Self-contained版ファイルコピー
Copy-Item -Path "./publish/host-win-x64/*" -Destination "$selfContainedDir/host/" -Recurse
Copy-Item -Path "./publish/cli-win-x64/*" -Destination "$selfContainedDir/cli/" -Recurse

# Framework-dependent版のパッケージ作成
$frameworkDir = "./release-packages/ProcTail-$Version-framework-dependent"
New-Item -ItemType Directory -Force -Path $frameworkDir | Out-Null
New-Item -ItemType Directory -Force -Path "$frameworkDir/host" | Out-Null
New-Item -ItemType Directory -Force -Path "$frameworkDir/cli" | Out-Null

# Framework-dependent版ファイルコピー
Copy-Item -Path "./publish/host-framework-dependent/*" -Destination "$frameworkDir/host/" -Recurse
Copy-Item -Path "./publish/cli-framework-dependent/*" -Destination "$frameworkDir/cli/" -Recurse

# READMEコピー
if (Test-Path "README.md") {
    Copy-Item -Path "README.md" -Destination "$selfContainedDir/"
    Copy-Item -Path "README.md" -Destination "$frameworkDir/"
}

Write-Host "Release packages created successfully"