param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

Write-Host "Creating ZIP archives for version: $Version"

# ZIP archives作成
$selfContainedZip = "./release-packages/ProcTail-$Version-self-contained-win-x64.zip"
$frameworkZip = "./release-packages/ProcTail-$Version-framework-dependent-win-x64.zip"

Compress-Archive -Path "./release-packages/ProcTail-$Version-self-contained/*" -DestinationPath $selfContainedZip -Force
Compress-Archive -Path "./release-packages/ProcTail-$Version-framework-dependent/*" -DestinationPath $frameworkZip -Force

# チェックサム計算
$selfContainedHash = (Get-FileHash $selfContainedZip -Algorithm SHA256).Hash
$frameworkHash = (Get-FileHash $frameworkZip -Algorithm SHA256).Hash

# チェックサムファイル作成
$checksumContent = @"
SHA256 Checksums for ProcTail $Version

ProcTail-$Version-self-contained-win-x64.zip: $selfContainedHash
ProcTail-$Version-framework-dependent-win-x64.zip: $frameworkHash
"@

$checksumContent | Out-File -FilePath "./release-packages/checksums.txt" -Encoding UTF8

Write-Host "ZIP archives and checksums created successfully"
Write-Host "Files created:"
Write-Host "  - $selfContainedZip"
Write-Host "  - $frameworkZip" 
Write-Host "  - ./release-packages/checksums.txt"