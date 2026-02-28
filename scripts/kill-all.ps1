# kill-all.ps1
# Kills all running instances of AIUsageTracker Monitor and UIs,
# and reports success/failure per process.

$targets = @(
    "AIUsageTracker.Monitor",
    "AIUsageTracker.UI",
    "AIUsageTracker.UI.Slim",
    # Legacy process name
    "AIConsumptionTracker.Agent",
    # Build and runtime processes that can become zombies
    "dotnet",
    "MSBuild"
)

$killed = New-Object System.Collections.Generic.List[string]
$failed = New-Object System.Collections.Generic.List[string]

foreach ($name in $targets)
{
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if (-not $procs)
    {
        continue
    }

    foreach ($proc in $procs)
    {
        $label = "$($proc.ProcessName) (PID $($proc.Id))"
        Write-Host "Attempting to kill $label ..."

        try
        {
            Stop-Process -Id $proc.Id -Force -ErrorAction Stop
            $killed.Add($label)
            Write-Host "  OK: killed $label"
        }
        catch
        {
            $message = "${label}: $($_.Exception.Message)"
            $failed.Add($message)
            Write-Host "  FAILED: $message"
        }
    }
}

if ($killed.Count -eq 0 -and $failed.Count -eq 0)
{
    Write-Host "No matching processes found."
    exit 0
}

if ($killed.Count -gt 0)
{
    Write-Host ""
    Write-Host "Killed process(es):"
    foreach ($entry in $killed)
    {
        Write-Host "  - $entry"
    }
}

if ($failed.Count -gt 0)
{
    Write-Host ""
    Write-Host "Could not kill process(es):"
    foreach ($entry in $failed)
    {
        Write-Host "  - $entry"
    }

    Write-Host ""
    Write-Host "Tip: try running this script in an elevated PowerShell session."
}

$remaining = @()
foreach ($name in $targets)
{
    $remaining += Get-Process -Name $name -ErrorAction SilentlyContinue
}

if ($remaining.Count -gt 0)
{
    Write-Host ""
    Write-Host "Still running after kill attempt:"
    $remaining |
        Sort-Object ProcessName, Id |
        ForEach-Object { Write-Host "  - $($_.ProcessName) (PID $($_.Id))" }
}

Write-Host ""
Write-Host "Done. Killed $($killed.Count) process(es); failed $($failed.Count)."


