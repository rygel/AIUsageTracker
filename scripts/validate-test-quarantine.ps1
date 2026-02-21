param(
    [string]$QuarantineFile = ".github\test-quarantine.txt"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $QuarantineFile)) {
    throw "Quarantine file not found: $QuarantineFile"
}

$today = (Get-Date).Date
$errors = New-Object System.Collections.Generic.List[string]
$seenTests = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::Ordinal)
$entryCount = 0

$lineNumber = 0
foreach ($rawLine in Get-Content -LiteralPath $QuarantineFile) {
    $lineNumber++
    $line = $rawLine.Trim()

    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#", [System.StringComparison]::Ordinal)) {
        continue
    }

    $entryCount++
    $parts = $line -split "\|"
    $testName = $parts[0].Trim()

    if ([string]::IsNullOrWhiteSpace($testName)) {
        $errors.Add("Line ${lineNumber}: missing fully-qualified test name.")
        continue
    }

    if (-not $seenTests.Add($testName)) {
        $errors.Add("Line ${lineNumber}: duplicate test entry '$testName'.")
    }

    $metadata = @{}
    for ($i = 1; $i -lt $parts.Length; $i++) {
        $segment = $parts[$i].Trim()
        if ([string]::IsNullOrWhiteSpace($segment)) {
            continue
        }

        $kv = $segment -split "=", 2
        if ($kv.Length -ne 2 -or [string]::IsNullOrWhiteSpace($kv[0]) -or [string]::IsNullOrWhiteSpace($kv[1])) {
            $errors.Add("Line ${lineNumber}: invalid metadata segment '$segment'. Use key=value.")
            continue
        }

        $metadata[$kv[0].Trim().ToLowerInvariant()] = $kv[1].Trim()
    }

    if (-not $metadata.ContainsKey("owner")) {
        $errors.Add("Line ${lineNumber}: missing owner metadata for '$testName'.")
    }

    if (-not $metadata.ContainsKey("expires")) {
        $errors.Add("Line ${lineNumber}: missing expires metadata for '$testName'.")
        continue
    }

    $expiry = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($metadata["expires"], "yyyy-MM-dd", [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::None, [ref]$expiry)) {
        $errors.Add("Line ${lineNumber}: invalid expires date '$($metadata["expires"])' for '$testName'. Expected yyyy-MM-dd.")
        continue
    }

    if ($expiry.Date -lt $today) {
        $errors.Add("Line ${lineNumber}: quarantine expired for '$testName' on $($expiry.ToString("yyyy-MM-dd")).")
    }
}

if ($errors.Count -gt 0) {
    Write-Host "ERROR: quarantine file validation failed:" -ForegroundColor Red
    foreach ($error in $errors) {
        Write-Host "  - $error" -ForegroundColor Red
    }
    exit 1
}

Write-Host "SUCCESS: quarantine file is valid ($entryCount active entr$(if ($entryCount -eq 1) { 'y' } else { 'ies' }))." -ForegroundColor Green
