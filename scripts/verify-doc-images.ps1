$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$docsRoot = Join-Path $projectRoot "docs"

$markdownFiles = @()
$readmePath = Join-Path $projectRoot "README.md"
if (Test-Path -LiteralPath $readmePath) {
    $markdownFiles += $readmePath
}

if (Test-Path -LiteralPath $docsRoot) {
    $markdownFiles += (Get-ChildItem -Path $docsRoot -Filter *.md -File -Recurse | Select-Object -ExpandProperty FullName)
}

if ($markdownFiles.Count -eq 0) {
    Write-Host "No markdown files found to validate." -ForegroundColor Yellow
    exit 0
}

$markdownImagePattern = '!\[[^\]]*\]\((?<target>[^)]+)\)'
$htmlImagePattern = '<img\b[^>]*\bsrc\s*=\s*["''](?<target>[^"'']+)["''][^>]*>'
$ignoredSchemes = '^(https?:|data:|mailto:|#)'
$brokenReferences = New-Object System.Collections.Generic.List[string]

function Normalize-ImageTarget {
    param([string]$Target)

    $value = $Target.Trim()
    if ($value.StartsWith("<") -and $value.EndsWith(">")) {
        $value = $value.Substring(1, $value.Length - 2)
    }
    else {
        $value = $value -replace '\s+["''][^"'']*["'']\s*$', ''
    }

    return $value.Trim()
}

function Test-TargetExists {
    param(
        [string]$SourceFile,
        [string]$Target
    )

    if ([string]::IsNullOrWhiteSpace($Target) -or $Target -match $ignoredSchemes) {
        return $true
    }

    $cleanTarget = ($Target -split '#')[0]
    $cleanTarget = ($cleanTarget -split '\?')[0]
    $cleanTarget = $cleanTarget.Replace('/', '\')

    $candidates = @()

    if ($cleanTarget.StartsWith('\')) {
        $candidates += Join-Path $projectRoot $cleanTarget.TrimStart('\')
    }
    else {
        $sourceDir = Split-Path -Parent $SourceFile
        $candidates += Join-Path $sourceDir $cleanTarget
        $candidates += Join-Path $projectRoot $cleanTarget
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (Test-Path -LiteralPath $candidate) {
            return $true
        }
    }

    return $false
}

foreach ($file in $markdownFiles) {
    $content = Get-Content -LiteralPath $file -Raw
    $relativeFile = $file.Substring($projectRoot.Length + 1)

    foreach ($match in [regex]::Matches($content, $markdownImagePattern)) {
        $target = Normalize-ImageTarget -Target $match.Groups["target"].Value
        if (-not (Test-TargetExists -SourceFile $file -Target $target)) {
            $brokenReferences.Add("$relativeFile -> $target")
        }
    }

    foreach ($match in [regex]::Matches($content, $htmlImagePattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
        $target = Normalize-ImageTarget -Target $match.Groups["target"].Value
        if (-not (Test-TargetExists -SourceFile $file -Target $target)) {
            $brokenReferences.Add("$relativeFile -> $target")
        }
    }
}

if ($brokenReferences.Count -gt 0) {
    Write-Host "ERROR: Broken image references detected:" -ForegroundColor Red
    foreach ($reference in $brokenReferences) {
        Write-Host "  - $reference" -ForegroundColor Red
    }
    exit 1
}

Write-Host "SUCCESS: README/docs image references are valid." -ForegroundColor Green
