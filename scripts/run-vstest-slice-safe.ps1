param(
    [Parameter(Mandatory = $true)]
    [string]$AssemblyPath,
    [Parameter(Mandatory = $true)]
    [string]$TestCaseFilter,
    [int]$TimeoutSeconds = 20,
    [string]$ResultsDirectory = ".tmp\vstest-slices",
    [string]$LogName = "slice.log"
)

$ErrorActionPreference = "Stop"

if ($TimeoutSeconds -lt 1) {
    throw "TimeoutSeconds must be >= 1."
}

if (-not (Test-Path -LiteralPath $AssemblyPath)) {
    throw "Assembly not found: $AssemblyPath"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$resultsRoot = Join-Path $projectRoot $ResultsDirectory
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null

$stdoutPath = Join-Path $resultsRoot ($LogName + ".stdout")
$stderrPath = Join-Path $resultsRoot ($LogName + ".stderr")
if (Test-Path -LiteralPath $stdoutPath) { Remove-Item -LiteralPath $stdoutPath -Force }
if (Test-Path -LiteralPath $stderrPath) { Remove-Item -LiteralPath $stderrPath -Force }

function Stop-ProcessTreeSafe {
    param([int]$ProcessId)

    if ($ProcessId -le 0) {
        return
    }

    try {
        & cmd /c "taskkill /PID $ProcessId /T /F" | Out-Null
    }
    catch {
        Write-Warning "taskkill failed for PID ${ProcessId}: $($_.Exception.Message)"
    }

    try {
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }
    catch {
        Write-Warning "Stop-Process failed for PID ${ProcessId}: $($_.Exception.Message)"
    }
}

$arguments = @(
    "vstest",
    $AssemblyPath,
    "--TestCaseFilter:$TestCaseFilter",
    "--logger:console;verbosity=normal"
)

$process = Start-Process `
    -FilePath "dotnet" `
    -ArgumentList $arguments `
    -PassThru `
    -WorkingDirectory $projectRoot `
    -RedirectStandardOutput $stdoutPath `
    -RedirectStandardError $stderrPath

$timedOut = $false
try {
    Wait-Process -Id $process.Id -Timeout $TimeoutSeconds -ErrorAction Stop
}
catch {
    $process.Refresh()
    if (-not $process.HasExited) {
        $timedOut = $true
    }
}

if ($timedOut) {
    Stop-ProcessTreeSafe -ProcessId $process.Id
    Write-Host "TIMEOUT after ${TimeoutSeconds}s" -ForegroundColor Yellow
}
else {
    $process.Refresh()
}

if (Test-Path -LiteralPath $stdoutPath) {
    Get-Content -LiteralPath $stdoutPath
}

if (Test-Path -LiteralPath $stderrPath) {
    Get-Content -LiteralPath $stderrPath
}

if ($timedOut) {
    exit 124
}

exit $process.ExitCode
