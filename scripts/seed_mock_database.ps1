$ErrorActionPreference = "Stop"

$appData = [Environment]::GetFolderPath("LocalApplicationData")
$dbDir = Join-Path $appData "AIConsumptionTracker"
if (!(Test-Path $dbDir)) {
    New-Item -ItemType Directory -Force -Path $dbDir | Out-Null
}

$dbPath = Join-Path $dbDir "usage.db"
if (Test-Path $dbPath) {
    Remove-Item $dbPath -Force
}

Write-Host "Creating mock database at: $dbPath"

# Note: sqlite3 is available on GitHub Actions windows-latest
# Define the schema
$schema = @"
CREATE TABLE providers (
    provider_id TEXT PRIMARY KEY,
    provider_name TEXT,
    plan_type TEXT,
    auth_source TEXT,
    account_name TEXT,
    updated_at TEXT,
    is_active INTEGER,
    config_json TEXT
);

CREATE TABLE provider_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT,
    requests_used REAL,
    requests_available REAL,
    requests_percentage REAL,
    is_available INTEGER,
    status_message TEXT,
    next_reset_time TEXT,
    fetched_at TEXT,
    details_json TEXT
);
"@

$schemaFile = "schema.sql"
Set-Content -Path $schemaFile -Value $schema
sqlite3 $dbPath ".read $schemaFile"
Remove-Item $schemaFile

# Generate mock data consistent with Slim UI deterministic mock
# Time variables formatting for SQLite 'yyyy-mm-dd hh:mm:ss'
$now = Get-Date

# Helper to escape quotes for SQLite
function Escape-Sql([string]$value) {
    if ([string]::IsNullOrEmpty($value)) { return "NULL" }
    return "'" + $value.Replace("'", "''") + "'"
}

$queries = @()

# Helpers
function Add-Provider($id, $name, $plan, $auth, $active) {
    $isActive = if ($active) { 1 } else { 0 }
    return "INSERT INTO providers (provider_id, provider_name, plan_type, auth_source, is_active, updated_at) VALUES ('$id', '$name', '$plan', '$auth', $isActive, CURRENT_TIMESTAMP);"
}

function Add-History($id, $used, $avail, $perc, $availFlag, $msg, $nextResetHours, $hoursAgo, $details) {
    $isAvail = if ($availFlag) { 1 } else { 0 }
    $fetched = $now.AddHours(-$hoursAgo).ToString("s")
    $nextReset = if ($nextResetHours -ne $null) { "'" + $now.AddHours($nextResetHours).ToString("s") + "'" } else { "NULL" }
    $detailsVal = if ($details) { Escape-Sql $details } else { "NULL" }
    
    return "INSERT INTO provider_history (provider_id, requests_used, requests_available, requests_percentage, is_available, status_message, next_reset_time, fetched_at, details_json) 
            VALUES ('$id', $used, $avail, $perc, $isAvail, '$msg', $nextReset, '$fetched', $detailsVal);"
}

# 1. Antigravity
$antigravityDetails = '[{"Name":"Claude Opus 4.6 (Thinking)","ModelName":"Claude Opus 4.6 (Thinking)","GroupName":"Recommended Group 1","Used":"60%","Description":"60% remaining"},{"Name":"Gemini 3 Flash","ModelName":"Gemini 3 Flash","GroupName":"Recommended Group 1","Used":"100%","Description":"100% remaining"}]'
$queries += Add-Provider "antigravity" "Antigravity" "coding" "local app" $true
$queries += Add-History "antigravity" 40 100 60 $true "60.0% Remaining" 10 0 $antigravityDetails
$queries += Add-History "antigravity" 38 100 62 $true "62.0% Remaining" 10 2 $null
$queries += Add-History "antigravity" 35 100 65 $true "65.0% Remaining" 10 4 $null
$queries += Add-History "antigravity" 30 100 70 $true "70.0% Remaining" 10 6 $null
$queries += Add-History "antigravity" 10 100 90 $true "90.0% Remaining" 10 12 $null

# 2. GitHub Copilot
$queries += Add-Provider "github-copilot" "GitHub Copilot" "coding" "oauth" $true
$queries += Add-History "github-copilot" 110 400 72.5 $true "72.5% Remaining" 20 0 $null
$queries += Add-History "github-copilot" 105 400 73.7 $true "73.7% Remaining" 20 2 $null
$queries += Add-History "github-copilot" 100 400 75.0 $true "75.0% Remaining" 20 4 $null
$queries += Add-History "github-copilot" 90 400 77.5 $true "77.5% Remaining" 20 8 $null

# 3. Z.AI
$queries += Add-Provider "zai-coding-plan" "Z.AI" "coding" "api key" $true
$queries += Add-History "zai-coding-plan" 45 250 82.0 $true "82.0% Remaining" 12 0 $null
$queries += Add-History "zai-coding-plan" 40 250 84.0 $true "84.0% Remaining" 12 2 $null
$queries += Add-History "zai-coding-plan" 30 250 88.0 $true "88.0% Remaining" 12 6 $null

# 4. Claude Code
$queries += Add-Provider "claude-code" "Claude Code" "usage" "local credentials" $true
$queries += Add-History "claude-code" 0 0 0 $true "Connected" $null 0 $null

# 5. Synthetic
$queries += Add-Provider "synthetic" "Synthetic" "coding" "api key" $true
$queries += Add-History "synthetic" 18 200 91.0 $true "91.0% Remaining" 4 0 $null
$queries += Add-History "synthetic" 15 200 92.5 $true "92.5% Remaining" 4 2 $null
$queries += Add-History "synthetic" 10 200 95.0 $true "95.0% Remaining" 4 6 $null

# 6. Mistral
$queries += Add-Provider "mistral" "Mistral" "usage" "api key" $true
$queries += Add-History "mistral" 0 0 0 $true "Connected" $null 0 $null

# 7. OpenAI - Show Pay As You Go usage history
$queries += Add-Provider "openai" "OpenAI" "usage" "api key" $true
$queries += Add-History "openai" 12.45 40.0 31.1 $true "`$12.45 / `$40.00" $null 0 $null
$queries += Add-History "openai" 10.00 40.0 25.0 $true "`$10.00 / `$40.00" $null 8 $null
$queries += Add-History "openai" 5.50 40.0 13.7 $true "`$5.50 / `$40.00" $null 16 $null

$queriesFile = "seed.sql"
Set-Content -Path $queriesFile -Value ($queries -join "`n")
sqlite3 $dbPath ".read $queriesFile"
Remove-Item $queriesFile

Write-Host "Seeded sqlite database successfully."
