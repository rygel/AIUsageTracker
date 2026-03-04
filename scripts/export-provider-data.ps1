# Export real provider data from Monitor database for CI testing
# Run this locally after running the Monitor with real API keys to generate test fixtures

param(
    [string]$OutputPath = "test-fixtures/provider-data.json"
)

$ErrorActionPreference = "Stop"

$appData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$dbPath = Join-Path $appData "AIUsageTracker\usage.db"

if (-not (Test-Path $dbPath)) {
    Write-Host "Database not found at: $dbPath" -ForegroundColor Red
    Write-Host "Please run the Monitor first to generate real data." -ForegroundColor Yellow
    exit 1
}

Write-Host "Reading from: $dbPath" -ForegroundColor Cyan

# Run the Seeder in export mode
dotnet run --project "$PSScriptRoot/Seeder/Seeder.csproj" --no-build -- export "$OutputPath"
$connection = New-Object Microsoft.Data.Sqlite.SqliteConnection($connectionString)
$connection.Open()

# Export providers
$providersCmd = $connection.CreateCommand()
$providersCmd.CommandText = "SELECT provider_id, provider_name, account_name, is_active, config_json FROM providers"
$providers = @()
$reader = $providersCmd.ExecuteReader()
while ($reader.Read()) {
    $providers += @{
        provider_id = $reader["provider_id"]
        provider_name = $reader["provider_name"]
        account_name = $reader["account_name"]
        is_active = $reader["is_active"]
        config_json = $reader["config_json"]
    }
}
$reader.Close()

# Export provider_history (latest 10 entries per provider)
$historyCmd = $connection.CreateCommand()
$historyCmd.CommandText = @"
SELECT h.provider_id, h.requests_used, h.requests_available, h.requests_percentage, 
       h.is_available, h.status_message, h.next_reset_time, h.fetched_at, h.details_json
FROM provider_history h
INNER JOIN (
    SELECT provider_id, MAX(fetched_at) as max_fetched
    FROM provider_history
    GROUP BY provider_id
) latest ON h.provider_id = latest.provider_id AND h.fetched_at = latest.max_fetched
ORDER BY h.provider_id
"@

$history = @()
$reader = $historyCmd.ExecuteReader()
while ($reader.Read()) {
    $history += @{
        provider_id = $reader["provider_id"]
        requests_used = $reader["requests_used"]
        requests_available = $reader["requests_available"]
        requests_percentage = $reader["requests_percentage"]
        is_available = $reader["is_available"]
        status_message = $reader["status_message"]
        next_reset_time = $reader["next_reset_time"]
        fetched_at = $reader["fetched_at"]
        details_json = $reader["details_json"]
    }
}
$reader.Close()

$connection.Close()

# Also get some historical data points (not just latest)
$connection.Open()
$historyAllCmd = $connection.CreateCommand()
$historyAllCmd.CommandText = @"
SELECT provider_id, requests_used, requests_available, requests_percentage,
       is_available, status_message, next_reset_time, fetched_at, details_json
FROM provider_history
WHERE fetched_at >= datetime('now', '-7 days')
ORDER BY provider_id, fetched_at DESC
"@

$historyAll = @()
$reader = $historyAllCmd.ExecuteReader()
while ($reader.Read()) {
    $historyAll += @{
        provider_id = $reader["provider_id"]
        requests_used = $reader["requests_used"]
        requests_available = $reader["requests_available"]
        requests_percentage = $reader["requests_percentage"]
        is_available = $reader["is_available"]
        status_message = $reader["status_message"]
        next_reset_time = $reader["next_reset_time"]
        fetched_at = $reader["fetched_at"]
        details_json = $reader["details_json"]
    }
}
$reader.Close()
$connection.Close()

$export = @{
    exported_at = (Get-Date).ToString("o")
    providers = $providers
    latest_history = $history
    history_7days = $historyAll
}

# Ensure output directory exists
$outputDir = Split-Path $OutputPath -Parent
if ($outputDir -and -not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

$export | ConvertTo-Json -Depth 10 | Set-Content $OutputPath -Encoding UTF8

Write-Host "Exported to: $OutputPath" -ForegroundColor Green
Write-Host "  Providers: $($providers.Count)" -ForegroundColor Gray
Write-Host "  Latest history: $($history.Count)" -ForegroundColor Gray
Write-Host "  7-day history: $($historyAll.Count)" -ForegroundColor Gray
