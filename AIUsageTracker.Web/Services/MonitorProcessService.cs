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
        var info = await MonitorLauncher.GetAndValidateMonitorInfoAsync().ConfigureAwait(false);
        if (info != null)
        {
            return (true, info.Port, $"Healthy on port {info.Port}.", null);
        }

        var (isRunning, port) = await MonitorLauncher.IsAgentRunningWithPortAsync().ConfigureAwait(false);
        if (isRunning)
        {
            return (true, port, $"Healthy on port {port}.", null);
        }

        var infoPath = this.ResolveExistingAgentInfoPath();
        if (infoPath == null)
        {
            return (false, port, "Monitor info file not found. Start Monitor to initialize it.", "agent-info-missing");
        }

        return (false, port, $"Monitor not reachable on port {port}.", "monitor-unreachable");
    }

    public async Task<bool> StartAgentAsync()
    {
        var detailed = await this.StartAgentDetailedAsync().ConfigureAwait(false);
        return detailed.Success;
    }

    public async Task<(bool Success, string Message)> StartAgentDetailedAsync()
    {
        var status = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
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

        var updated = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
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
        var status = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
        if (!status.IsRunning && status.Error == "agent-info-missing")
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

    private string? ResolveExistingAgentInfoPath()
    {
        var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return MonitorInfoPathCatalog.GetReadCandidatePaths(appDataRoot, userProfileRoot)
            .ToList()
            .Where(File.Exists)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .FirstOrDefault();
    }
}
