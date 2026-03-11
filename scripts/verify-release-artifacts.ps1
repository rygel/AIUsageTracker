param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [ValidateSet("stable", "beta")]
    [string]$Channel = "stable",
    [string]$ArtifactsDir = "artifacts",
    [string]$AppcastDir = "appcast",
    [string[]]$WindowsArchitectures = @("x64", "x86", "arm64"),
    [switch]$SkipArtifactChecks
)

$ErrorActionPreference = "Stop"
$sparkleNamespace = "http://www.andymatuschak.org/xml-namespaces/sparkle"

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-Sha256Hex {
    param([string]$Path)

    return (Get-FileHash -Path $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-AppcastPrefix {
    param([string]$SelectedChannel)

    if ($SelectedChannel -eq "beta") {
        return "appcast_beta"
    }

    return "appcast"
}

$appcastPrefix = Get-AppcastPrefix -SelectedChannel $Channel
Write-Host "Verifying release artifacts for version $Version ($Channel channel)..." -ForegroundColor Cyan

if (-not $SkipArtifactChecks) {
    Assert-True (Test-Path -LiteralPath $ArtifactsDir) "Artifacts directory '$ArtifactsDir' does not exist."
}

Assert-True (Test-Path -LiteralPath $AppcastDir) "Appcast directory '$AppcastDir' does not exist."

$installerFiles = @()
$zipFiles = @()

if (-not $SkipArtifactChecks) {
    foreach ($arch in $WindowsArchitectures) {
        $runtime = "win-$arch"
        $installerName = "AIUsageTracker_Setup_v$Version" + "_$runtime.exe"
        $zipName = "AIUsageTracker_v$Version" + "_$runtime.zip"

        $installerPath = Join-Path $ArtifactsDir $installerName
        $zipPath = Join-Path $ArtifactsDir $zipName

        Assert-True (Test-Path -LiteralPath $installerPath) "Missing installer artifact: $installerName"
        Assert-True (Test-Path -LiteralPath $zipPath) "Missing zip artifact: $zipName"

        $installerInfo = Get-Item -LiteralPath $installerPath
        $zipInfo = Get-Item -LiteralPath $zipPath

        Assert-True ($installerInfo.Length -gt 0) "Installer has zero length: $installerName"
        Assert-True ($zipInfo.Length -gt 0) "Zip has zero length: $zipName"

        $installerFiles += $installerInfo
        $zipFiles += $zipInfo
    }
}

$appcastFiles = @()
foreach ($arch in $WindowsArchitectures) {
    if ($arch -eq "x64") {
        $appcastFiles += [pscustomobject]@{
            Arch = "x64"
            Path = Join-Path $AppcastDir "$appcastPrefix.xml"
            IsDefault = $true
        }
    }

    $appcastFiles += [pscustomobject]@{
        Arch = $arch
        Path = Join-Path $AppcastDir "$($appcastPrefix)_$arch.xml"
        IsDefault = $false
    }
}

foreach ($entry in $appcastFiles) {
    Assert-True (Test-Path -LiteralPath $entry.Path) "Missing appcast file: $($entry.Path)"

    [xml]$xml = Get-Content -LiteralPath $entry.Path -Raw
    $nsManager = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $nsManager.AddNamespace("sparkle", $sparkleNamespace)

    $item = $xml.SelectSingleNode("/rss/channel/item", $nsManager)
    Assert-True ($null -ne $item) "Appcast item missing in $($entry.Path)"

    $enclosure = $item.SelectSingleNode("enclosure", $nsManager)
    Assert-True ($null -ne $enclosure) "Appcast enclosure missing in $($entry.Path)"

    $expectedInstallerName = "AIUsageTracker_Setup_v$Version" + "_win-$($entry.Arch).exe"
    $expectedUrl = "https://github.com/rygel/AIUsageTracker/releases/download/v$Version/$expectedInstallerName"
    $expectedReleaseNotes = "https://github.com/rygel/AIUsageTracker/releases/tag/v$Version"

    $actualUrl = [string]$enclosure.GetAttribute("url")
    $actualShortVersion = [string]$enclosure.GetAttribute("shortVersionString", $sparkleNamespace)
    $releaseNotesNode = $item.SelectSingleNode("sparkle:releaseNotesLink", $nsManager)
    $actualReleaseNotes = [string]$releaseNotesNode.InnerText

    Assert-True ($actualUrl -eq $expectedUrl) "Unexpected enclosure url in $($entry.Path). Expected '$expectedUrl', got '$actualUrl'."
    Assert-True ($actualShortVersion -eq $Version) "Unexpected sparkle:shortVersionString in $($entry.Path). Expected '$Version', got '$actualShortVersion'."
    Assert-True ($actualReleaseNotes -eq $expectedReleaseNotes) "Unexpected release notes link in $($entry.Path). Expected '$expectedReleaseNotes', got '$actualReleaseNotes'."
}

if (-not $SkipArtifactChecks) {
    $checksumLines = @()
    $allFiles = @($installerFiles + $zipFiles)
    foreach ($file in $allFiles | Sort-Object Name) {
        $checksumLines += "$(Get-Sha256Hex -Path $file.FullName)  $($file.Name)"
    }

    $manifestPath = Join-Path $ArtifactsDir "release-checksums-v$Version.txt"
    Set-Content -LiteralPath $manifestPath -Value $checksumLines
    Write-Host "Wrote checksum manifest: $manifestPath" -ForegroundColor Green
}

Write-Host "Release artifact verification passed for $Version ($Channel)." -ForegroundColor Green
