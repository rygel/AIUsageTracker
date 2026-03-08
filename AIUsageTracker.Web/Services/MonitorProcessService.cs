using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

public class MonitorProcessService
{
    private readonly ILogger<MonitorProcessService> _logger;

    public readonly record struct MonitorStatusResult(bool IsRunning, int Port, string Message, string? Error);

    public readonly record struct MonitorActionResult(bool Success, string Message);

    public MonitorProcessService(ILogger<MonitorProcessService> logger)
    {
        this._logger = logger;
    }

    public async Task<(bool IsRunning, int Port)> GetAgentStatusAsync()
    {
        var detailed = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
        return (detailed.IsRunning, detailed.Port);
    }

    public async Task<MonitorStatusResult> GetAgentStatusDetailedAsync()
    {
        var status = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        return new MonitorStatusResult(status.IsRunning, status.Port, status.Message, status.Error);
    }

    public async Task<bool> StartAgentAsync()
    {
        var detailed = await this.StartAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<MonitorActionResult> StartAgentDetailedAsync()
    {
        var status = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (status.IsRunning)
        {
            return new MonitorActionResult(true, $"Monitor already running on port {status.Port}.");
        }

        var started = await MonitorLauncher.EnsureAgentRunningAsync().ConfigureAwait(false);
        if (!started)
        {
            this._logger.LogWarning("Monitor failed to reach a healthy state after startup request.");
            return new MonitorActionResult(false, "Failed to start monitor or monitor did not become healthy.");
        }

        var updated = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (updated.IsRunning)
        {
            return new MonitorActionResult(true, $"Monitor started on port {updated.Port}.");
        }

        return new MonitorActionResult(false, $"Start requested, but monitor status is still unavailable. {updated.Message}");
    }

    public async Task<bool> StopAgentAsync()
    {
        var detailed = await this.StopAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<MonitorActionResult> StopAgentDetailedAsync()
    {
        var status = await MonitorLauncher.GetAgentStatusInfoAsync().ConfigureAwait(false);
        if (!status.IsRunning && string.Equals(status.Error, "agent-info-missing", StringComparison.Ordinal))
        {
            return new MonitorActionResult(true, "Monitor already stopped (info file missing).");
        }

        var stopped = await MonitorLauncher.StopAgentAsync().ConfigureAwait(false);
        if (stopped)
        {
            return new MonitorActionResult(true, $"Monitor stopped on port {status.Port}.");
        }

        this._logger.LogWarning("Monitor stop request failed.");
        return new MonitorActionResult(false, "Failed to stop monitor.");
    }
}
