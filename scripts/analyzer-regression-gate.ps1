param(
    [string]$BaseRef = "origin/develop",
    [string]$Solution = "AIUsageTracker.sln",
    [string]$Configuration = "Debug",
    [string]$AllowList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (git rev-parse --show-toplevel).Trim()
Set-Location $repoRoot

function Get-AnalyzerWarnings {
    param(
        [string]$SourceRoot,
        [string]$SolutionPath,
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

        Write-Host "==> Restoring solution: $SolutionPath"
        & dotnet restore $SolutionPath
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed for $Label"
        }

        Write-Host "==> Building solution for analyzer baseline: $SolutionPath"
        & dotnet build $SolutionPath --configuration $ConfigurationName --no-restore "/flp:logfile=$logPath;verbosity=normal"
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed for $Label"
        }
    }
    finally {
        Pop-Location
    }

    if (-not (Test-Path $logPath)) {
        throw "No analyzer build log found: $logPath"
    }

    # Convert MSBuild warning records to stable keys: ID|File|Line
    $raw = Get-Content $logPath
    Write-Output $raw | Tee-Object -FilePath $outPath | Out-Null
    $warningPattern = "^(?<file>.+?)\\((?<line>\\d+),\\d+\\):\\s+warning\\s+(?<id>(?:MA|VSTHRD|CA|IDE|RS|SYSLIB)\\d+):\\s"

    $warnings = @{}
    foreach ($lineText in $raw) {
        if ($lineText -match $warningPattern) {
            $file = $matches.file -replace "\\", "/"
            $lineNumber = $matches.line
            $id = $matches.id
            if ([System.IO.Path]::IsPathRooted($file)) {
                $fullPath = [System.IO.Path]::GetFullPath($file)
                $rootPath = [System.IO.Path]::GetFullPath((Join-Path $SourceRoot "."))
                if ($fullPath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
                    $file = $fullPath.Substring($rootPath.Length).TrimStart("\").TrimStart("/")
                    $file = $file -replace "\\", "/"
                }
            }
            $path = $file.Trim()
            $key = "$id|$path|$lineNumber"
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

Write-Host "=== Analyzer baseline reference: $BaseRef"

$baseRef = if ([string]::IsNullOrWhiteSpace($BaseRef)) { "origin/develop" } else { $BaseRef }
$changedFiles = git diff --name-only "$baseRef...HEAD" -- "*.cs" "*.xaml" "*.csproj" "*.props" "*.targets"
if (-not $changedFiles) {
    Write-Host "No analyzer-relevant files changed. Skipping regression check."
    exit 0
}

$changedSet = Normalize-PathTokens -Paths $changedFiles

New-Item -ItemType Directory -Path (Join-Path $repoRoot ".analyzer-gate-output") -Force | Out-Null
$worktreePath = Join-Path $repoRoot ".analyzer-gate-baseline"
if (Test-Path $worktreePath) {
    Remove-Item $worktreePath -Recurse -Force
}

git worktree add --detach $worktreePath $baseRef | Out-Null
try {
    $baseWarnings = Get-AnalyzerWarnings -SourceRoot $worktreePath -SolutionPath $Solution -ConfigurationName $Configuration -Label "base"
    $headWarnings = Get-AnalyzerWarnings -SourceRoot $repoRoot -SolutionPath $Solution -ConfigurationName $Configuration -Label "head"
}
finally {
    git worktree remove --force $worktreePath
}

$baseSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($warning in $baseWarnings) {
    $baseSet.Add($warning) | Out-Null
}

$newWarnings = @()
foreach ($warning in $headWarnings) {
    $parts = $warning.Split("|", 3)
    if ($parts.Length -lt 3) {
        continue
    }
    $file = $parts[1]
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
    "Analyzer warnings introduced by this PR on files changed from $baseRef:",
    ""
) + ($newWarnings | Sort-Object) | Set-Content $newListPath

$grouped = $newWarnings | ForEach-Object {
    $parts = $_.Split("|", 3)
    [pscustomobject]@{
        Id = $parts[0]
        File = $parts[1]
        Line = $parts[2]
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
