param(
    [string[]]$Providers = @(),
    [string]$OutputDir = "test-fixtures"
)

$ErrorActionPreference = "Stop"

$timestamp = Get-Date -Format "yyyy-MM-ddTHH-mm-ss"
$OutputDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $PSScriptRoot "..\$OutputDir" }

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

function Get-CodexToken {
    $authPath = Join-Path $env:USERPROFILE ".codex\auth.json"
    if (-not (Test-Path $authPath)) {
        Write-Host "[Codex] Auth file not found: $authPath" -ForegroundColor Yellow
        return $null
    }
    
    $authJson = Get-Content $authPath -Raw | ConvertFrom-Json
    if ($authJson.tokens -and $authJson.tokens.access_token) {
        return $authJson.tokens.access_token
    }
    
    Write-Host "[Codex] No access token found in auth file" -ForegroundColor Yellow
    return $null
}

function Invoke-ProviderRequest {
    param(
        [string]$Name,
        [string]$ApiKey,
        [string]$Endpoint,
        [hashtable]$Headers = @{},
        [string]$Method = "GET"
    )
    
    try {
        Write-Host "[$Name] Fetching from $Endpoint..." -ForegroundColor Cyan
        
        $params = @{
            Uri = $Endpoint
            Method = $Method
            Headers = $Headers
            ErrorAction = "Stop"
        }
        
        $response = Invoke-RestMethod @params
        
        $filename = Join-Path $OutputDir "$($Name.ToLower())-$timestamp.json"
        $response | ConvertTo-Json -Depth 15 | Out-File -FilePath $filename -Encoding UTF8
        
        Write-Host "[$Name] Saved to $filename" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "[$Name] Error: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# For providers where we don't have env vars, try to read from Monitor's running API
# This requires the Monitor to be running and have fetched data recently

function Get-FromMonitorApi {
    param([string]$ProviderId)
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:5000/api/usage/$ProviderId" -ErrorAction SilentlyContinue
        return $response
    }
    catch {
        return $null
    }
}

$availableProviders = @{
    "codex" = {
        $token = Get-CodexToken
        if ($token) {
            Invoke-ProviderRequest -Name "Codex" -ApiKey $token -Endpoint "https://chatgpt.com/backend-api/wham/usage" -Headers @{ "Authorization" = "Bearer $token" }
        }
    }
    
    "kimi" = {
        $apiKey = $env:KIMI_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "Kimi" -ApiKey $apiKey -Endpoint "https://api.kimi.com/coding/v1/usages" -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[Kimi] KIMI_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "anthropic" = {
        $apiKey = $env:ANTHROPIC_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "Anthropic" -ApiKey $apiKey -Endpoint "https://api.anthropic.com/v1/usage" -Headers @{ "x-api-key" = $apiKey; "anthropic-version" = "2023-06-01" }
        }
        else { Write-Host "[Anthropic] ANTHROPIC_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "openai" = {
        $apiKey = $env:OPENAI_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "OpenAI" -ApiKey $apiKey -Endpoint "https://api.openai.com/v1/usage" -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[OpenAI] OPENAI_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "openrouter" = {
        $apiKey = $env:OPENROUTER_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "OpenRouter" -ApiKey $apiKey -Endpoint "https://openrouter.ai/api/v1/credits" -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[OpenRouter] OPENROUTER_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "mistral" = {
        $apiKey = $env:MISTRAL_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "Mistral" -ApiKey $apiKey -Endpoint "https://api.mistral.ai/v1/me" -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[Mistral] MISTRAL_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "deepseek" = {
        $apiKey = $env:DEEPSEEK_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "DeepSeek" -ApiKey $apiKey -Endpoint "https://api.deepseek.com/user/balance" -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[DeepSeek] DEEPSEEK_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "zai" = {
        $apiKey = $env:ZAI_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "Zai" -ApiKey $apiKey -Endpoint "https://api.z.ai/api/monitor/usage/quota/limit" -Headers @{ "Authorization" = $apiKey; "Accept-Language" = "en-US,en" }
        }
        else { Write-Host "[Zai] ZAI_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "xiaomi" = {
        $apiKey = $env:XIAOMI_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "Xiaomi" -ApiKey $apiKey -Endpoint "https://api.xiaomimimo.com/v1/user/balance" -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[Xiaomi] XIAOMI_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "synthetic" = {
        $apiKey = $env:SYNTHETIC_API_KEY
        if ($apiKey) {
            $endpoint = "https://account.synthetic.ai/api/usage"
            Invoke-ProviderRequest -Name "Synthetic" -ApiKey $apiKey -Endpoint $endpoint -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[Synthetic] SYNTHETIC_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "opencode" = {
        $apiKey = $env:OPENCODE_API_KEY
        if ($apiKey) {
            $endpoint = "https://opencode.ai/api/usage"
            Invoke-ProviderRequest -Name "OpenCode" -ApiKey $apiKey -Endpoint $endpoint -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[OpenCode] OPENCODE_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "minimax" = {
        $apiKey = $env:MINIMAX_API_KEY
        if ($apiKey) {
            $endpoint = "https://api.minimax.chat/v1/user/balance"
            Invoke-ProviderRequest -Name "Minimax" -ApiKey $apiKey -Endpoint $endpoint -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[Minimax] MINIMAX_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "github-copilot" = {
        $apiKey = $env:GITHUB_TOKEN
        if ($apiKey) {
            $endpoint = "https://api.github.com/copilot_internal/usage"
            Invoke-ProviderRequest -Name "GitHubCopilot" -ApiKey $apiKey -Endpoint $endpoint -Headers @{ "Authorization" = "Bearer $apiKey"; "Accept" = "application/vnd.github.copilot-internal+json" }
        }
        else { Write-Host "[GitHub Copilot] GITHUB_TOKEN not set" -ForegroundColor Yellow }
    }
    
    "claude-code" = {
        $apiKey = $env:ANTHROPIC_API_KEY
        if ($apiKey) {
            Invoke-ProviderRequest -Name "ClaudeCode" -ApiKey $apiKey -Endpoint "https://api.anthropic.com/v1/claude_code_usage" -Headers @{ "x-api-key" = $apiKey; "anthropic-version" = "2023-06-01" }
        }
        else { Write-Host "[ClaudeCode] ANTHROPIC_API_KEY not set" -ForegroundColor Yellow }
    }
    
    "antigravity" = {
        $apiKey = $env:ANTIGRAVITY_API_KEY
        if ($apiKey) {
            $endpoint = "https://antigravity.dev/api/usage"
            Invoke-ProviderRequest -Name "Antigravity" -ApiKey $apiKey -Endpoint $endpoint -Headers @{ "Authorization" = "Bearer $apiKey" }
        }
        else { Write-Host "[Antigravity] ANTIGRAVITY_API_KEY not set" -ForegroundColor Yellow }
    }
}

Write-Host "=== AI Provider API Debug Tool ===" -ForegroundColor Magenta
Write-Host "Timestamp: $timestamp" -ForegroundColor Gray
Write-Host "Output: $OutputDir" -ForegroundColor Gray
Write-Host ""

if ($Providers.Count -eq 0) {
    Write-Host "Fetching all available providers..." -ForegroundColor Cyan
    $Providers = $availableProviders.Keys | Sort-Object
}
else {
    Write-Host "Fetching providers: $($Providers -join ', ')" -ForegroundColor Cyan
}

$success = 0
$failed = 0

foreach ($provider in $Providers) {
    $providerLower = $provider.ToLower()
    if ($availableProviders.ContainsKey($providerLower)) {
        Write-Host ""
        & $availableProviders[$providerLower]
        $success++
    }
    else {
        Write-Host "[$provider] Unknown provider" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Magenta
Write-Host "Success: $success" -ForegroundColor Green
Write-Host "Failed/Not configured: $failed" -ForegroundColor $(if ($failed -gt 0) { "Yellow" } else { "Green" })
