param(
    [ValidateSet("start", "stop", "restart", "status")]
    [string]$Action = "start",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$NoBuild,
    [switch]$ShowMonitorWindow
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$monitorProject = Join-Path $projectRoot "AIUsageTracker.Monitor\AIUsageTracker.Monitor.csproj"
$slimProject = Join-Path $projectRoot "AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj"
$monitorExe = Join-Path $projectRoot "AIUsageTracker.Monitor\bin\$Configuration\net8.0\AIUsageTracker.Monitor.exe"
$slimExe = Join-Path $projectRoot "AIUsageTracker.UI.Slim\bin\$Configuration\net8.0-windows10.0.17763.0\AIUsageTracker.exe"
$monitorJsonPath = Join-Path $env:LOCALAPPDATA "AIUsageTracker\monitor.json"

function Set-StableBuildEnvironment
{
    $env:MSBuildEnableWorkloadResolver = "false"
    $env:MSBUILDDISABLENODEREUSE = "1"
    $env:DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER = "1"
}

function Build-Targets
{
    Write-Host "Building monitor and slim UI ($Configuration)..."
    Set-StableBuildEnvironment

    dotnet build $monitorProject --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Monitor build failed."
    }

    dotnet build $slimProject --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0)
    {
        throw "Slim UI build failed."
    }
}

function Get-MonitorProcessIdFromFile
{
    if (-not (Test-Path $monitorJsonPath))
    {
        return $null
    }

    try
    {
        $info = Get-Content $monitorJsonPath -Raw | ConvertFrom-Json
        if ($null -ne $info -and $null -ne $info.ProcessId)
        {
            return [int]$info.ProcessId
        }
    }
    catch
    {
        Write-Warning "Could not parse monitor.json: $($_.Exception.Message)"
    }

    return $null
}

function Stop-ByProcessName
{
    param([string]$Name)

    $procs = @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
    foreach ($proc in $procs)
    {
        try
        {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            Write-Host "Stopped $Name (PID $($proc.Id))"
        }
        catch
        {
            Write-Warning "Failed to stop $Name (PID $($proc.Id)): $($_.Exception.Message)"
        }
    }
}

function Stop-Processes
{
    $monitorPid = Get-MonitorProcessIdFromFile
    if ($null -ne $monitorPid)
    {
        try
        {
            $monitorProc = Get-Process -Id $monitorPid -ErrorAction Stop
            Stop-Process -Id $monitorPid -Force -ErrorAction Stop
            Write-Host "Stopped monitor (PID $($monitorProc.Id))"
        }
        catch
        {
            Write-Warning "Failed to stop monitor from monitor.json PID ${monitorPid}: $($_.Exception.Message)"
        }
    }

    Stop-ByProcessName -Name "AIUsageTracker.Monitor"
    Stop-ByProcessName -Name "AIUsageTracker"
}

function Wait-ForMonitorReady
{
    $timeout = [TimeSpan]::FromSeconds(20)
    $start = Get-Date

    while ((Get-Date) - $start -lt $timeout)
    {
        if (Test-Path $monitorJsonPath)
        {
            try
            {
                $info = Get-Content $monitorJsonPath -Raw | ConvertFrom-Json
                if ($null -ne $info -and $null -ne $info.Port -and [int]$info.Port -gt 0)
                {
                    return [int]$info.Port
                }
            }
            catch
            {
                # ignore parse errors while file is being written
            }
        }

        Start-Sleep -Milliseconds 300
    }

    throw "Monitor did not become ready within $($timeout.TotalSeconds) seconds."
}

function Start-Processes
{
    if (-not $NoBuild)
    {
        Build-Targets
    }

    if (-not (Test-Path $monitorExe))
    {
        throw "Monitor executable not found: $monitorExe"
    }

    if (-not (Test-Path $slimExe))
    {
        throw "Slim executable not found: $slimExe"
    }

    Write-Host "Stopping existing monitor/UI instances first..."
    Stop-Processes

    Write-Host "Starting monitor..."
    $monitorWindowStyle = if ($ShowMonitorWindow) { "Normal" } else { "Hidden" }
    $monitorProc = Start-Process -FilePath $monitorExe -WorkingDirectory (Split-Path $monitorExe -Parent) -WindowStyle $monitorWindowStyle -PassThru
    Write-Host "Monitor started (PID $($monitorProc.Id))"

    $port = Wait-ForMonitorReady
    Write-Host "Monitor ready on port $port"

    Write-Host "Starting Slim UI..."
    $slimProc = Start-Process -FilePath $slimExe -WorkingDirectory (Split-Path $slimExe -Parent) -PassThru
    Write-Host "Slim UI started (PID $($slimProc.Id))"
}

function Show-Status
{
    $monitorProcs = @(Get-Process -Name "AIUsageTracker.Monitor" -ErrorAction SilentlyContinue)
    $uiProcs = @(Get-Process -Name "AIUsageTracker" -ErrorAction SilentlyContinue)

    if ($monitorProcs.Count -eq 0)
    {
        Write-Host "Monitor: not running"
    }
    else
    {
        Write-Host ("Monitor: running ({0})" -f (($monitorProcs | ForEach-Object { "PID $($_.Id)" }) -join ", "))
    }

    if ($uiProcs.Count -eq 0)
    {
        Write-Host "Slim UI: not running"
    }
    else
    {
        Write-Host ("Slim UI: running ({0})" -f (($uiProcs | ForEach-Object { "PID $($_.Id)" }) -join ", "))
    }

    if (Test-Path $monitorJsonPath)
    {
        Write-Host "monitor.json: $monitorJsonPath"
        Write-Host (Get-Content $monitorJsonPath -Raw)
    }
    else
    {
        Write-Host "monitor.json: not found"
    }
}

switch ($Action)
{
    "start"
    {
        Start-Processes
    }
    "stop"
    {
        Stop-Processes
    }
    "restart"
    {
        Stop-Processes
        Start-Processes
    }
    "status"
    {
        Show-Status
    }
}
