param()

$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")

Set-Location $repoRoot
git config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) {
    throw "Failed to configure git hooks path."
}

Write-Host "Configured local git hooks path: .githooks"
Write-Host "Pre-commit hook is now active."
