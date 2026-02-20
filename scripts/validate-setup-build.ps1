param(
    [string]$Version = "",
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

function Get-TrackerVersion {
    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        return $Version
    }

    if (-not (Test-Path "Directory.Build.props")) {
        throw "Directory.Build.props not found. Pass -Version explicitly."
    }

    $propsContent = Get-Content "Directory.Build.props" -Raw
    if ($propsContent -match "<TrackerVersion>(.*?)</TrackerVersion>") {
        return $matches[1]
    }

    throw "Unable to resolve <TrackerVersion> from Directory.Build.props."
}

function Get-IsccPath {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    return $null
}

function New-PublishFixture {
    param(
        [string]$SourceDir
    )

    New-Item -ItemType Directory -Path $SourceDir -Force | Out-Null

    Set-Content (Join-Path $SourceDir "README.md") "Fixture README" -NoNewline
    Set-Content (Join-Path $SourceDir "LICENSE") "Fixture LICENSE" -NoNewline

    $componentFiles = @(
        "Tracker\AIConsumptionTracker.exe",
        "Agent\AIConsumptionTracker.Agent.exe",
        "Web\AIConsumptionTracker.Web.exe",
        "CLI\AIConsumptionTracker.CLI.exe"
    )

    foreach ($relativePath in $componentFiles) {
        $filePath = Join-Path $SourceDir $relativePath
        $fileDir = Split-Path $filePath -Parent
        New-Item -ItemType Directory -Path $fileDir -Force | Out-Null
        Set-Content $filePath "fixture" -NoNewline
    }
}

$resolvedVersion = Get-TrackerVersion
$iscc = Get-IsccPath
if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found. Install Inno Setup 6 first."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("AITracker-setup-validate-" + [Guid]::NewGuid().ToString("N"))
$outputDir = Join-Path $tempRoot "output"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$architectures = @(
    @{ Runtime = "win-x64"; Arch = "x64" },
    @{ Runtime = "win-x86"; Arch = "x86" },
    @{ Runtime = "win-arm64"; Arch = "arm64" }
)

try {
    foreach ($arch in $architectures) {
        $sourceDir = Join-Path $tempRoot ("publish-" + $arch.Runtime)
        New-PublishFixture -SourceDir $sourceDir

        Write-Host "Validating setup compile for $($arch.Runtime)..." -ForegroundColor Cyan
        & $iscc "scripts\setup.iss" "/Qp" "/O$outputDir" "/DSourcePath=$sourceDir" "/DMyAppVersion=$resolvedVersion" "/DMyAppArch=$($arch.Arch)"
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup compile failed for $($arch.Runtime)."
        }
    }

    $generated = Get-ChildItem (Join-Path $outputDir "AIConsumptionTracker_Setup_v$resolvedVersion`_*.exe") -ErrorAction SilentlyContinue
    if (($generated | Measure-Object).Count -lt 3) {
        throw "Expected setup executables for x64/x86/arm64 were not generated."
    }

    Write-Host "Setup validation passed for version $resolvedVersion." -ForegroundColor Green
}
finally {
    if (-not $KeepTemp -and (Test-Path $tempRoot)) {
        Remove-Item -Path $tempRoot -Recurse -Force
    }
}
