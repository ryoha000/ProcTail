# Start CLI and subscribe to notepad process
# CLIを起動してnotepadプロセスを購読

param(
    [Parameter(Mandatory=$true)]
    [int]$NotepadPid,
    [string]$CliPath = "",
    [string]$Tag = "test-notepad"
)

# Calculate absolute path if not provided
if (-not $CliPath) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $projectRoot = Split-Path -Parent (Split-Path -Parent $scriptDir)
    $CliPath = Join-Path $projectRoot "publish\cli\proctail.exe"
}

Write-Host "Starting ProcTail CLI and subscribing to notepad (PID: $NotepadPid)..." -ForegroundColor Yellow

# Resolve full path to CLI executable
Write-Host "Looking for CLI at: $CliPath" -ForegroundColor Gray
$fullCliPath = if (Test-Path $CliPath) { $CliPath } else { $null }
if (-not $fullCliPath) {
    Write-Host "Could not find ProcTail CLI at: $CliPath" -ForegroundColor Red
    Write-Host "Please ensure the application has been built and published." -ForegroundColor Red
    exit 1
}

Write-Host "Using CLI from: $fullCliPath" -ForegroundColor Cyan

# Add watch target for notepad process
Write-Host "Adding watch target for notepad PID $NotepadPid with tag '$Tag'..." -ForegroundColor Cyan

try {
    $addResult = & $fullCliPath add --pid $NotepadPid --tag $Tag 2>&1
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Successfully added watch target for notepad!" -ForegroundColor Green
        Write-Host $addResult
    } else {
        Write-Host "Failed to add watch target: $addResult" -ForegroundColor Red
        exit 1
    }
}
catch {
    Write-Host "Error running CLI add-watch-target command: $_" -ForegroundColor Red
    exit 1
}

Write-Host "CLI subscription completed successfully!" -ForegroundColor Green