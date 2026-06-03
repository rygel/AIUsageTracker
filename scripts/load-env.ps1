$envFile = Join-Path $PSScriptRoot ".." ".env"
if (-not (Test-Path $envFile)) {
    Write-Error ".env file not found at $envFile"
    exit 1
}
Get-Content $envFile | Where-Object { $_ -match "^[^#]" -and $_ -match "=" } | ForEach-Object {
    $name, $value = $_ -split "=", 2
    Set-Item -Path "env:$name" -Value $value
    Write-Host "Set $name"
}
