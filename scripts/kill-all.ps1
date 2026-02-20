# kill-all.ps1
# Kills all running instances of AIConsumptionTracker Agent and UIs.

$targets = @(
    "AIConsumptionTracker.Agent",
    "AIConsumptionTracker.UI",
    "AIConsumptionTracker.UI.Slim"
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
    Write-Host "No AIConsumptionTracker processes found."
} else {
    Write-Host "Done. Killed $killed process(es)."
}
