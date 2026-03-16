param(
    [Parameter(Mandatory = $true)]
    [string]$RootPath,

    [Parameter(Mandatory = $true)]
    [string]$ProjectName,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyName
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path -LiteralPath $RootPath
$candidatePaths = @(
    "$ProjectName\bin\Debug\net8.0-windows10.0.17763.0\$AssemblyName",
    "$ProjectName\bin\Debug\net8.0\$AssemblyName",
    "$ProjectName\bin\Debug\$AssemblyName",
    "$ProjectName\$AssemblyName"
) | ForEach-Object { Join-Path $root $_ }

foreach ($candidate in $candidatePaths) {
    if (Test-Path -LiteralPath $candidate) {
        $candidate
        exit 0
    }
}

$found = Get-ChildItem -Path $root -Recurse -Filter $AssemblyName -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*$ProjectName*" } |
    Select-Object -First 1

if ($found) {
    $found.FullName
    exit 0
}

$joinedCandidates = $candidatePaths -join ", "
throw "Test assembly not found. Checked: $joinedCandidates"
