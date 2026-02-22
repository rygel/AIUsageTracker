# kill-all.ps1
# Kills all running instances of AIUsageTracker Monitor and UIs.

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

$killed = 0

foreach ($name in $targets) {
    $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
    if ($procs) {
        $procs | ForEach-Object {
            Write-Host "Killing $($_.Name) (PID $($_.Id))..."
            $_ | Stop-Process -Force
            $killed++
        }
    }
}

if ($killed -eq 0) {
    Write-Host "No AIUsageTracker processes found."
} else {
    Write-Host "Done. Killed $killed process(es)."
}


