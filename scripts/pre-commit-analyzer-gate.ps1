#!/usr/bin/env pwsh

param(
    [string]$AgentOwner = "git-pre-commit",
    [string]$AgentTask = "changed-file-analyzer-gate"
)

$ErrorActionPreference = "Stop"
$env:AGENT_OWNER = $AgentOwner
$env:AGENT_TASK = $AgentTask

$repoRoot = (& git rev-parse --show-toplevel).Trim()
if (-not $repoRoot)
{
    throw "Could not determine the repository root."
}

Set-Location $repoRoot

$pathSpecs = @("*.cs", "*.csproj", "*.props", "*.targets", ".editorconfig")
$stagedFiles = @(& git diff --cached --name-only --diff-filter=ACMR -- $pathSpecs) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

if ($stagedFiles.Count -eq 0)
{
    Write-Host "Analyzer gate: no staged C# or analyzer-configuration files."
    exit 0
}

$unstagedFiles = @(& git diff --name-only --diff-filter=ACMR -- $pathSpecs) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
$partiallyStagedFiles = @($unstagedFiles | Where-Object { $stagedFiles -contains $_ })

if ($partiallyStagedFiles.Count -gt 0)
{
    Write-Error @"
Analyzer gate refused partially staged analyzer-relevant files:
$($partiallyStagedFiles -join [Environment]::NewLine)

Stage each listed file completely or split it in a separate worktree before committing.
"@
    exit 1
}

$sourceFiles = @($stagedFiles | Where-Object { $_.EndsWith(".cs", [StringComparison]::OrdinalIgnoreCase) })
if ($sourceFiles.Count -gt 0)
{
    $commonArguments = @(
        "AIUsageTracker.sln",
        "--verify-no-changes",
        "--verbosity", "minimal",
        "--include"
    ) + $sourceFiles

    Write-Host "Analyzer gate: checking whitespace in $($sourceFiles.Count) staged C# file(s)..."
    & dotnet format whitespace @commonArguments
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    Write-Host "Analyzer gate: checking code style..."
    & dotnet format style @commonArguments --severity warn
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }

    Write-Host "Analyzer gate: checking analyzer findings..."
    & dotnet format analyzers @commonArguments --severity warn
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

Write-Host "Analyzer gate: building the Release solution..."
& dotnet build AIUsageTracker.sln --configuration Release --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host "Analyzer gate: running core tests..."
& dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Release --no-build --verbosity quiet
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host "Analyzer gate: running Monitor tests..."
& dotnet test AIUsageTracker.Monitor.Tests/AIUsageTracker.Monitor.Tests.csproj --configuration Release --no-build --verbosity quiet
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

Write-Host "Analyzer gate passed."
