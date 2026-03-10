param(
    [string]$BaseRef = "origin/develop",
    [string]$Solution = "AIUsageTracker.sln",
    [string]$Configuration = "Debug",
    [string]$AllowList,
    [string]$ScopeConfig = ".analyzer-gate/scope.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location $repoRoot

function Get-RelativeRepoPath {
    param(
        [string]$SourceRoot,
        [string]$PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $PathValue
    }

    $normalized = $PathValue -replace "\\", "/"
    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        $fullPath = [System.IO.Path]::GetFullPath($PathValue)
        $rootPath = [System.IO.Path]::GetFullPath((Join-Path $SourceRoot "."))
        if ($fullPath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
            $normalized = $fullPath.Substring($rootPath.Length).TrimStart("\").TrimStart("/")
        }
        else {
            $normalized = $fullPath
        }
    }

    return $normalized.Trim().Replace("\", "/")
}

function Get-AnalyzerWarnings {
    param(
        [string]$SourceRoot,
        [string[]]$BuildTargets,
        [string]$ConfigurationName,
        [string]$Label
    )

    $workRoot = Join-Path $SourceRoot ".analyzer-gate"
    $logRoot = Join-Path $workRoot "logs"
    New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
    $logPath = Join-Path $logRoot "${Label}-build.log"
    $outPath = Join-Path $logRoot "${Label}-build-output.txt"

    Push-Location $SourceRoot
    try {
        if (Test-Path $logPath) {
            Remove-Item $logPath -Force
        }
        if (Test-Path $outPath) {
            Remove-Item $outPath -Force
        }

        foreach ($target in $BuildTargets) {
            Write-Host "==> Restoring target: $target"
            & dotnet restore $target
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet restore failed for $Label target '$target'"
            }

            Write-Host "==> Building target for analyzer baseline: $target"
            & dotnet build $target --configuration $ConfigurationName --no-restore "/flp:logfile=$logPath;verbosity=normal;append=true"
            if ($LASTEXITCODE -ne 0) {
                throw "dotnet build failed for $Label target '$target'"
            }
        }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path $logPath)) {
        throw "No analyzer build log found: $logPath"
    }

    # Convert MSBuild warning records to stable keys: ID|Project|File|Line
    $raw = Get-Content $logPath
    Write-Output $raw | Tee-Object -FilePath $outPath | Out-Null
    $warningPattern = "^(?<file>.+?)\\((?<line>\\d+),\\d+\\):\\s+warning\\s+(?<id>(?:MA|VSTHRD|CA|IDE|RS|SYSLIB)\\d+):\\s.*(?:\\[(?<project>.+?\\.csproj)\\])?$"

    $warnings = @{}
    foreach ($lineText in $raw) {
        if ($lineText -match $warningPattern) {
            $file = $matches.file -replace "\\", "/"
            $lineNumber = $matches.line
            $id = $matches.id
            $project = if ($matches.project) { $matches.project } else { "unknown" }
            $path = Get-RelativeRepoPath -SourceRoot $SourceRoot -PathValue $file
            $projectPath = Get-RelativeRepoPath -SourceRoot $SourceRoot -PathValue $project
            $key = "$id|$projectPath|$path|$lineNumber"
            if (-not $warnings.ContainsKey($key)) {
                $warnings[$key] = $true
            }
        }
    }

    return $warnings.Keys
}

function Normalize-PathTokens {
    param([string[]]$Paths)

    $set = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($pathText in $Paths) {
        if ([string]::IsNullOrWhiteSpace($pathText)) {
            continue
        }
        $set.Add(($pathText.Trim().TrimStart("./").Replace("\", "/"))) | Out-Null
    }
    return $set
}

function Convert-ToPathPrefixesSet {
    param([object[]]$Prefixes)

    $result = @()
    foreach ($prefix in $Prefixes) {
        if ($null -eq $prefix) {
            continue
        }

        $normalized = $prefix.ToString().Trim()
        if ([string]::IsNullOrWhiteSpace($normalized)) {
            continue
        }

        $normalized = $normalized.Replace("\", "/").TrimStart("./")
        $result += $normalized
    }

    return $result
}

function Matches-TrackedPrefix {
    param(
        [string]$PathValue,
        [string[]]$Prefixes
    )

    foreach ($prefix in $Prefixes) {
        if ($PathValue.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

Write-Host "=== Analyzer baseline reference: $BaseRef"

$baseRef = if ([string]::IsNullOrWhiteSpace($BaseRef)) { "origin/develop" } else { $BaseRef }
$changedFiles = git diff --name-only "$baseRef...HEAD" -- "*.cs" "*.xaml" "*.csproj" "*.props" "*.targets"
if (-not $changedFiles) {
    Write-Host "No analyzer-relevant files changed. Skipping regression check."
    exit 0
}

$changedSet = Normalize-PathTokens -Paths $changedFiles
$buildTargets = @($Solution)

if (-not [string]::IsNullOrWhiteSpace($ScopeConfig) -and (Test-Path $ScopeConfig)) {
    $scopeJson = Get-Content $ScopeConfig -Raw | ConvertFrom-Json
    $trackedPrefixes = Convert-ToPathPrefixesSet -Prefixes $scopeJson.trackedPathPrefixes
    $globalTriggers = Convert-ToPathPrefixesSet -Prefixes $scopeJson.globalTriggerPaths
    $configuredTargets = Convert-ToPathPrefixesSet -Prefixes $scopeJson.buildTargets

    $hasGlobalTriggerChange = $false
    foreach ($path in $changedSet) {
        if (Matches-TrackedPrefix -PathValue $path -Prefixes $globalTriggers) {
            $hasGlobalTriggerChange = $true
            break
        }
    }

    if (-not $hasGlobalTriggerChange -and $trackedPrefixes.Count -gt 0) {
        $scopedChanged = @()
        foreach ($path in $changedSet) {
            if (Matches-TrackedPrefix -PathValue $path -Prefixes $trackedPrefixes) {
                $scopedChanged += $path
            }
        }

        if ($scopedChanged.Count -eq 0) {
            Write-Host "No scoped analyzer paths changed. Skipping regression check."
            exit 0
        }

        $changedSet = Normalize-PathTokens -Paths $scopedChanged
        if ($configuredTargets.Count -gt 0) {
            $buildTargets = $configuredTargets
        }
    }
    elseif ($hasGlobalTriggerChange) {
        Write-Host "Global analyzer trigger changed. Running full-solution analyzer regression gate."
    }
}

Write-Host "Analyzer build targets:"
foreach ($target in $buildTargets) {
    Write-Host "  - $target"
}

New-Item -ItemType Directory -Path (Join-Path $repoRoot ".analyzer-gate-output") -Force | Out-Null
$worktreePath = Join-Path $repoRoot ".analyzer-gate-baseline"
if (Test-Path $worktreePath) {
    Remove-Item $worktreePath -Recurse -Force
}

git worktree add --detach $worktreePath $baseRef | Out-Null
$baseWarnings = @()
$headWarnings = @()
$baselineBuildSucceeded = $true
try {
    try {
        $baseWarnings = Get-AnalyzerWarnings -SourceRoot $worktreePath -BuildTargets $buildTargets -ConfigurationName $Configuration -Label "base"
    }
    catch {
        $baselineBuildSucceeded = $false
        Write-Warning "Analyzer baseline build failed for '$baseRef'. Skipping regression diff for this run."
        Write-Warning $_
    }

    $headWarnings = Get-AnalyzerWarnings -SourceRoot $repoRoot -BuildTargets $buildTargets -ConfigurationName $Configuration -Label "head"
}
finally {
    git worktree remove --force $worktreePath
}

if (-not $baselineBuildSucceeded) {
    Write-Host "⚠️ Analyzer regression gate skipped because baseline build failed."
    exit 0
}

$baseSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($warning in $baseWarnings) {
    $baseSet.Add($warning) | Out-Null
}

$newWarnings = @()
foreach ($warning in $headWarnings) {
    $parts = $warning.Split("|", 4)
    if ($parts.Length -lt 4) {
        continue
    }
    $file = $parts[2]
    if (-not $changedSet.Contains($file)) {
        continue
    }
    if ($baseSet.Contains($warning)) {
        continue
    }
    if (-not [string]::IsNullOrWhiteSpace($AllowList) -and $warning -match $AllowList) {
        continue
    }
    $newWarnings += $warning
}

if ($newWarnings.Count -eq 0) {
    Write-Host "✅ No new analyzer warnings introduced on changed files."
    exit 0
}

$outputDir = Join-Path $repoRoot ".analyzer-gate-output"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$newListPath = Join-Path $outputDir "new-analyzer-warnings.txt"
$summaryPath = Join-Path $outputDir "analyzer-regression-summary.md"

@(
    "Analyzer warnings introduced by this PR on files changed from ${baseRef}:",
    ""
) + ($newWarnings | Sort-Object) | Set-Content $newListPath

$grouped = $newWarnings | ForEach-Object {
    $parts = $_.Split("|", 4)
    [pscustomobject]@{
        Id = $parts[0]
        Project = $parts[1]
        File = $parts[2]
        Line = $parts[3]
    }
}

$idGroups = $grouped | Group-Object -Property Id | Sort-Object Name
$sb = New-Object System.Text.StringBuilder
$sb.AppendLine("| Rule | Count |") | Out-Null
$sb.AppendLine("|------|-------|") | Out-Null
foreach ($grp in $idGroups) {
    $sb.AppendLine("| $($grp.Name) | $($grp.Count) |") | Out-Null
}
$summary = @(
    "# Analyzer Regression Gate",
    "",
    "Compared head against $baseRef for analyzer warnings on changed files.",
    "",
    $sb.ToString(),
    ""
)
$summary | Set-Content $summaryPath

Write-Host "⚠️ Analyzer warning regression detected ($($newWarnings.Count))."
Write-Host "New warnings:"
$newWarnings | Sort-Object | ForEach-Object { Write-Host "  - $_" }
Write-Host "Details: $newListPath"
Write-Host "Summary: $summaryPath"
exit 1
