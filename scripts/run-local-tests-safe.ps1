param(
    [ValidateSet("core", "monitor", "web")]
    [string[]]$Suites = @("core", "monitor", "web"),
    [string]$Configuration = "Debug",
    [int]$MaxParallel = 1,
    [int]$TotalTimeoutMinutes = 10,
    [string]$ResultsRoot = "TestResults/local-safe",
    [switch]$SkipBuild,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

if ($MaxParallel -lt 1) {
    throw "MaxParallel must be >= 1."
}

if ($TotalTimeoutMinutes -lt 1) {
    throw "TotalTimeoutMinutes must be >= 1."
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$runnerScript = Join-Path $PSScriptRoot "run-vstest-with-retry.ps1"
if (-not (Test-Path -LiteralPath $runnerScript)) {
    throw "Missing script: $runnerScript"
}

$suiteCatalog = @{
    core = [PSCustomObject]@{
        Key = "core"
        ProjectPath = "AIUsageTracker.Tests/AIUsageTracker.Tests.csproj"
        AssemblyName = "AIUsageTracker.Tests.dll"
        SuiteName = "core-tests-local"
        AttemptTimeoutMinutes = 2
        HangTimeoutSeconds = 75
        HardTimeoutSeconds = 180
    }
    monitor = [PSCustomObject]@{
        Key = "monitor"
        ProjectPath = "AIUsageTracker.Monitor.Tests/AIUsageTracker.Monitor.Tests.csproj"
        AssemblyName = "AIUsageTracker.Monitor.Tests.dll"
        SuiteName = "monitor-tests-local"
        AttemptTimeoutMinutes = 1
        HangTimeoutSeconds = 45
        HardTimeoutSeconds = 120
    }
    web = [PSCustomObject]@{
        Key = "web"
        ProjectPath = "AIUsageTracker.Web.Tests/AIUsageTracker.Web.Tests.csproj"
        AssemblyName = "AIUsageTracker.Web.Tests.dll"
        SuiteName = "web-tests-local"
        AttemptTimeoutMinutes = 2
        HangTimeoutSeconds = 60
        HardTimeoutSeconds = 180
    }
}

$selectedSuites = @()
foreach ($suiteKey in $Suites) {
    if (-not $suiteCatalog.ContainsKey($suiteKey)) {
        throw "Unknown suite: $suiteKey"
    }

    $selectedSuites += $suiteCatalog[$suiteKey]
}

$env:MSBUILDDISABLENODEREUSE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
# Local SDK/workload resolver state on some Windows machines can make project-reference
# evaluation fail before tests start. Disabling the workload resolver keeps safe local
# test runs deterministic without affecting CI.
$env:MSBuildEnableWorkloadResolver = "false"

function Resolve-AssemblyPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$AssemblyName
    )

    $projectDirectory = Split-Path -Parent $ProjectPath
    $searchRoot = Join-Path $projectDirectory ("bin/{0}" -f $Configuration)
    if (-not (Test-Path -LiteralPath $searchRoot)) {
        return $null
    }

    return Get-ChildItem -Path $searchRoot -Recurse -File -Filter $AssemblyName |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Stop-ProcessTreeSafe {
    param(
        [int]$ProcessId,
        [string]$Reason
    )

    if ($ProcessId -le 0) {
        return
    }

    Write-Host "Stopping process tree PID=$ProcessId ($Reason)" -ForegroundColor Yellow
    try {
        & cmd /c "taskkill /PID $ProcessId /T /F" | Out-Null
    }
    catch {
        Write-Host "taskkill failed for PID=${ProcessId}: $($_.Exception.Message)" -ForegroundColor Yellow
    }

    try {
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
    }
    catch {
        Write-Host "Stop-Process fallback failed for PID=${ProcessId}: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Stop-OrphanTestProcesses {
    param(
        [string]$Reason
    )

    $processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ProcessId -ne $PID -and
            (
                $_.Name -like "testhost*.exe" -or
                $_.Name -eq "vstest.console.exe" -or
                (
                    $_.Name -eq "dotnet.exe" -and
                    -not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
                    (
                        $_.CommandLine.IndexOf("vstest", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                        $_.CommandLine.IndexOf("testhost", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                        $_.CommandLine.IndexOf("AIUsageTracker.Tests", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                        $_.CommandLine.IndexOf("AIUsageTracker.Monitor.Tests", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
                        $_.CommandLine.IndexOf("AIUsageTracker.Web.Tests", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
                    )
                )
            )
        }

    foreach ($proc in $processes) {
        try {
            & cmd /c "taskkill /PID $($proc.ProcessId) /T /F" | Out-Null
            Write-Host "Killed orphan test process PID=$($proc.ProcessId) ($Reason)." -ForegroundColor Yellow
        }
        catch {
            Write-Host "Failed to kill orphan process PID=$($proc.ProcessId): $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

function Write-LogTail {
    param(
        [string]$Path,
        [int]$Lines = 80
    )

    if (Test-Path -LiteralPath $Path) {
        Write-Host "--- tail $Path ---" -ForegroundColor DarkYellow
        Get-Content -LiteralPath $Path -Tail $Lines
    }
}

function ConvertTo-NullableInt {
    param(
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    $parsed = 0
    if ([int]::TryParse($Value, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Get-LatestTrxSummary {
    param(
        [string]$ResultsDir,
        [string]$SuiteName
    )

    $trxFile = Get-ChildItem -LiteralPath $ResultsDir -Filter "$SuiteName*.trx" -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if (-not $trxFile) {
        return $null
    }

    try {
        [xml]$trx = Get-Content -LiteralPath $trxFile.FullName -Raw
        $counters = $trx.TestRun.ResultSummary.Counters
        if (-not $counters) {
            return $null
        }

        $failed = ConvertTo-NullableInt -Value ([string]$counters.failed)
        $errors = ConvertTo-NullableInt -Value ([string]$counters.error)
        $timeouts = ConvertTo-NullableInt -Value ([string]$counters.timeout)
        $total = ConvertTo-NullableInt -Value ([string]$counters.total)
        $passed = ConvertTo-NullableInt -Value ([string]$counters.passed)

        $isSuccess = ($failed -eq 0) -and ($errors -eq 0) -and ($timeouts -eq 0)
        return [PSCustomObject]@{
            TrxPath = $trxFile.FullName
            IsSuccess = $isSuccess
            Failed = $failed
            Errors = $errors
            Timeouts = $timeouts
            Total = $total
            Passed = $passed
        }
    }
    catch {
        Write-Host "Failed to parse TRX file '$($trxFile.FullName)': $($_.Exception.Message)" -ForegroundColor Yellow
        return $null
    }
}

Push-Location $projectRoot
try {
    Stop-OrphanTestProcesses -Reason "pre-run cleanup"

    if (-not $SkipBuild) {
        foreach ($suite in $selectedSuites) {
            Write-Host "Building $($suite.Key) suite project ($($suite.ProjectPath))..." -ForegroundColor Cyan
            if (-not $DryRun) {
                dotnet build $suite.ProjectPath --configuration $Configuration --disable-build-servers -m:1 -p:BuildInParallel=false
            }
        }
    }

    $resolvedSuites = @()
    foreach ($suite in $selectedSuites) {
        $assembly = Resolve-AssemblyPath -ProjectPath $suite.ProjectPath -AssemblyName $suite.AssemblyName
        if (-not $assembly) {
            if (-not $DryRun) {
                throw "Could not locate built assembly for suite '$($suite.Key)' ($($suite.AssemblyName))."
            }

            Write-Host ("DryRun: assembly not found for suite {0}, using placeholder path." -f $suite.Key) -ForegroundColor Yellow
            $assembly = [PSCustomObject]@{ FullName = "<not-built>" }
        }

        $resolvedSuites += [PSCustomObject]@{
            Key = $suite.Key
            SuiteName = $suite.SuiteName
            AssemblyPath = $assembly.FullName
            AttemptTimeoutMinutes = $suite.AttemptTimeoutMinutes
            HangTimeoutSeconds = $suite.HangTimeoutSeconds
            HardTimeoutSeconds = $suite.HardTimeoutSeconds
            ResultsDir = Join-Path $ResultsRoot $suite.Key
        }
    }

    foreach ($suite in $resolvedSuites) {
        Write-Host ("Prepared suite {0}: {1}" -f $suite.Key, $suite.AssemblyPath) -ForegroundColor DarkCyan
    }

    if ($DryRun) {
        Write-Host "DryRun enabled. Exiting without running tests." -ForegroundColor Green
        exit 0
    }

    $queue = [System.Collections.Generic.Queue[object]]::new()
    foreach ($suite in $resolvedSuites) {
        $queue.Enqueue($suite)
    }

    $running = @()
    $completed = @()
    $runStartTime = Get-Date

    while ($queue.Count -gt 0 -or $running.Count -gt 0) {
        if (((Get-Date) - $runStartTime).TotalMinutes -ge $TotalTimeoutMinutes) {
            foreach ($entry in $running) {
                Stop-ProcessTreeSafe -ProcessId $entry.Process.Id -Reason "global timeout ($($entry.Suite.Key))"
            }

            Stop-OrphanTestProcesses -Reason "global timeout cleanup"
            throw "Local test run exceeded total timeout of $TotalTimeoutMinutes minute(s)."
        }

        while ($queue.Count -gt 0 -and $running.Count -lt $MaxParallel) {
            $suite = $queue.Dequeue()
            New-Item -ItemType Directory -Path $suite.ResultsDir -Force | Out-Null

            $stdout = Join-Path $suite.ResultsDir "launcher-stdout.log"
            $stderr = Join-Path $suite.ResultsDir "launcher-stderr.log"
            if (Test-Path -LiteralPath $stdout) { Remove-Item -LiteralPath $stdout -Force -ErrorAction SilentlyContinue }
            if (Test-Path -LiteralPath $stderr) { Remove-Item -LiteralPath $stderr -Force -ErrorAction SilentlyContinue }

            $args = @(
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", $runnerScript,
                "-AssemblyPath", $suite.AssemblyPath,
                "-SuiteName", $suite.SuiteName,
                "-ResultsDirectory", $suite.ResultsDir,
                "-QuarantineFile", ".github/test-quarantine.txt",
                "-MaxRetries", "0",
                "-AttemptTimeoutMinutes", $suite.AttemptTimeoutMinutes.ToString(),
                "-HangTimeoutSeconds", $suite.HangTimeoutSeconds.ToString()
            )

            $proc = Start-Process -FilePath "pwsh" -ArgumentList $args -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            Write-Host ("Started suite {0} (PID={1}, hard timeout={2}s)" -f $suite.Key, $proc.Id, $suite.HardTimeoutSeconds) -ForegroundColor Cyan

            $running += [PSCustomObject]@{
                Suite = $suite
                Process = $proc
                StartTime = Get-Date
                StdoutPath = $stdout
                StderrPath = $stderr
                TimedOut = $false
            }
        }

        $stillRunning = @()
        foreach ($entry in $running) {
            $elapsedSeconds = ((Get-Date) - $entry.StartTime).TotalSeconds
            if ($entry.Process.HasExited) {
                $entry.Process.WaitForExit()
                $entry.Process.Refresh()

                $exitCode = $null
                if ($null -ne $entry.Process.ExitCode) {
                    try {
                        $exitCode = [int]$entry.Process.ExitCode
                    }
                    catch {
                        $exitCode = $null
                    }
                }

                $trxSummary = $null
                $exitCodeSource = "process"
                if ($null -eq $exitCode) {
                    $trxSummary = Get-LatestTrxSummary -ResultsDir $entry.Suite.ResultsDir -SuiteName $entry.Suite.SuiteName
                    if ($null -ne $trxSummary) {
                        $exitCodeSource = "trx"
                        $exitCode = if ($trxSummary.IsSuccess) { 0 } else { 1 }
                    }
                    else {
                        $exitCodeSource = "unknown"
                    }
                }

                $completed += [PSCustomObject]@{
                    Suite = $entry.Suite
                    ExitCode = $exitCode
                    ExitCodeSource = $exitCodeSource
                    TimedOut = $entry.TimedOut
                    StdoutPath = $entry.StdoutPath
                    StderrPath = $entry.StderrPath
                    TrxSummary = $trxSummary
                }
                continue
            }

            if ($elapsedSeconds -ge $entry.Suite.HardTimeoutSeconds) {
                $entry.TimedOut = $true
                Stop-ProcessTreeSafe -ProcessId $entry.Process.Id -Reason "suite timeout ($($entry.Suite.Key))"
                $entry.Process.Refresh()
                $completed += [PSCustomObject]@{
                    Suite = $entry.Suite
                    ExitCode = 124
                    ExitCodeSource = "timeout"
                    TimedOut = $true
                    StdoutPath = $entry.StdoutPath
                    StderrPath = $entry.StderrPath
                    TrxSummary = $null
                }
                Write-LogTail -Path $entry.StdoutPath
                Write-LogTail -Path $entry.StderrPath
                continue
            }

            $stillRunning += $entry
        }

        $running = $stillRunning
        Start-Sleep -Seconds 2
    }

    $failed = @()
    foreach ($result in $completed | Sort-Object { $_.Suite.Key }) {
        if ($result.ExitCode -eq 0 -and -not $result.TimedOut) {
            if ($result.ExitCodeSource -eq "trx" -and $null -ne $result.TrxSummary) {
                Write-Host ("PASS {0} (derived from TRX: passed={1}, total={2})" -f $result.Suite.Key, $result.TrxSummary.Passed, $result.TrxSummary.Total) -ForegroundColor Green
            }
            else {
                Write-Host ("PASS {0}" -f $result.Suite.Key) -ForegroundColor Green
            }
        }
        else {
            if ($result.ExitCodeSource -eq "trx" -and $null -ne $result.TrxSummary) {
                Write-Host ("FAIL {0} (ExitCode={1}, TimedOut={2}, source=TRX, failed={3}, errors={4}, timeouts={5})" -f $result.Suite.Key, $result.ExitCode, $result.TimedOut, $result.TrxSummary.Failed, $result.TrxSummary.Errors, $result.TrxSummary.Timeouts) -ForegroundColor Red
            }
            else {
                Write-Host ("FAIL {0} (ExitCode={1}, TimedOut={2}, source={3})" -f $result.Suite.Key, $result.ExitCode, $result.TimedOut, $result.ExitCodeSource) -ForegroundColor Red
            }
            Write-LogTail -Path $result.StdoutPath
            Write-LogTail -Path $result.StderrPath
            $failed += $result
        }
    }

    if ($failed.Count -gt 0) {
        exit 1
    }

    exit 0
}
finally {
    Stop-OrphanTestProcesses -Reason "post-run cleanup"
    Pop-Location
}
