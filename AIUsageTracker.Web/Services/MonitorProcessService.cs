using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

public class MonitorProcessService
{
    private readonly ILogger<MonitorProcessService> _logger;

    public MonitorProcessService(ILogger<MonitorProcessService> logger)
    {
        this._logger = logger;
    }

    public async Task<(bool IsRunning, int Port)> GetAgentStatusAsync()
    {
        var detailed = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
        return (detailed.IsRunning, detailed.Port);
    }

    public async Task<(bool IsRunning, int Port, string Message, string? Error)> GetAgentStatusDetailedAsync()
    {
        var status = await this.GetAgentStatusSnapshotAsync().ConfigureAwait(false);
        return (status.IsRunning, status.Port, status.Message, status.Error);
    }

    public async Task<bool> StartAgentAsync()
    {
        var detailed = await this.StartAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<(bool Success, string Message)> StartAgentDetailedAsync()
    {
        var status = await this.GetAgentStatusSnapshotAsync().ConfigureAwait(false);
        if (status.IsRunning)
        {
            return (true, $"Monitor already running on port {status.Port}.");
        }

        var started = await MonitorLauncher.EnsureAgentRunningAsync().ConfigureAwait(false);
        if (!started)
        {
            this._logger.LogWarning("Monitor failed to reach a healthy state after startup request.");
            return (false, "Failed to start monitor or monitor did not become healthy.");
        }

        var updated = await this.GetAgentStatusSnapshotAsync().ConfigureAwait(false);
        if (updated.IsRunning)
        {
            return (true, $"Monitor started on port {updated.Port}.");
        }

        return (false, $"Start requested, but monitor status is still unavailable. {updated.Message}");
    }

    public async Task<bool> StopAgentAsync()
    {
        var detailed = await this.StopAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<(bool Success, string Message)> StopAgentDetailedAsync()
    {
        var status = await this.GetAgentStatusSnapshotAsync().ConfigureAwait(false);
        if (!status.IsRunning && string.Equals(status.Error, AgentStatusSnapshot.InfoMissingError, StringComparison.Ordinal))
        {
            return (true, "Monitor already stopped (info file missing).");
        }

        var stopped = await MonitorLauncher.StopAgentAsync().ConfigureAwait(false);
        if (stopped)
        {
            return (true, $"Monitor stopped on port {status.Port}.");
        }

        this._logger.LogWarning("Monitor stop request failed.");
        return (false, "Failed to stop monitor.");
    }

    private async Task<AgentStatusSnapshot> GetAgentStatusSnapshotAsync()
    {
        var info = await MonitorLauncher.GetAndValidateMonitorInfoAsync().ConfigureAwait(false);
        if (info != null)
        {
            return AgentStatusSnapshot.Healthy(info.Port);
        }

        var (isRunning, port) = await MonitorLauncher.IsAgentRunningWithPortAsync().ConfigureAwait(false);
        if (isRunning)
        {
            return AgentStatusSnapshot.Healthy(port);
        }

        return MonitorInfoPathCatalog.ResolveExistingReadPath() == null
            ? AgentStatusSnapshot.InfoMissing(port)
            : AgentStatusSnapshot.Unreachable(port);
    }

    private readonly record struct AgentStatusSnapshot(bool IsRunning, int Port, string Message, string? Error)
    {
        public const string InfoMissingError = "agent-info-missing";
        public const string UnreachableError = "monitor-unreachable";

        public static AgentStatusSnapshot Healthy(int port)
        {
            return new(true, port, $"Healthy on port {port}.", null);
        }

        public static AgentStatusSnapshot InfoMissing(int port)
        {
            return new(false, port, "Monitor info file not found. Start Monitor to initialize it.", InfoMissingError);
        }

        public static AgentStatusSnapshot Unreachable(int port)
        {
            return new(false, port, $"Monitor not reachable on port {port}.", UnreachableError);
        }
    }
}
