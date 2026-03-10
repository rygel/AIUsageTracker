// <copyright file="MonitorProcessService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services
{
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.MonitorClient;

    public class MonitorProcessService
    {
        private readonly IMonitorLifecycleService _monitorLifecycleService;
        private readonly ILogger<MonitorProcessService> _logger;

        public readonly record struct MonitorStatusResult(bool IsRunning, int Port, string Message, string? Error);

        public MonitorProcessService(IMonitorLifecycleService monitorLifecycleService, ILogger<MonitorProcessService> logger)
        {
            this._monitorLifecycleService = monitorLifecycleService;
            this._logger = logger;
        }

        public async Task<(bool IsRunning, int Port)> GetAgentStatusAsync()
        {
            var detailed = await this.GetAgentStatusDetailedAsync().ConfigureAwait(false);
            return (detailed.IsRunning, detailed.Port);
        }

        public async Task<MonitorStatusResult> GetAgentStatusDetailedAsync()
        {
            var status = await this._monitorLifecycleService.GetAgentStatusInfoAsync().ConfigureAwait(false);
            return new MonitorStatusResult(status.IsRunning, status.Port, status.Message, status.Error);
        }

        public async Task<bool> StartAgentAsync()
        {
            var detailed = await this.StartAgentDetailedAsync().ConfigureAwait(false);
            return detailed.Success;
        }

        public async Task<MonitorActionResult> StartAgentDetailedAsync()
        {
            var status = await this._monitorLifecycleService.GetAgentStatusInfoAsync().ConfigureAwait(false);
            if (status.IsRunning)
            {
                return new MonitorActionResult { Success = true, Message = $"Monitor already running on port {status.Port}." };
            }

            var started = await this._monitorLifecycleService.EnsureAgentRunningAsync().ConfigureAwait(false);
            if (!started)
            {
                this._logger.LogWarning("Monitor failed to reach a healthy state after startup request.");
                return new MonitorActionResult { Success = false, Message = "Failed to start monitor or monitor did not become healthy." };
            }

            var updated = await this._monitorLifecycleService.GetAgentStatusInfoAsync().ConfigureAwait(false);
            if (updated.IsRunning)
            {
                return new MonitorActionResult { Success = true, Message = $"Monitor started on port {updated.Port}." };
            }

            return new MonitorActionResult { Success = false, Message = $"Start requested, but monitor status is still unavailable. {updated.Message}" };
        }

        public async Task<bool> StopAgentAsync()
        {
            var detailed = await this.StopAgentDetailedAsync().ConfigureAwait(false);
            return detailed.Success;
        }

        public async Task<MonitorActionResult> StopAgentDetailedAsync()
        {
            var status = await this._monitorLifecycleService.GetAgentStatusInfoAsync().ConfigureAwait(false);
            if (!status.IsRunning && string.Equals(status.Error, "agent-info-missing", StringComparison.Ordinal))
            {
                return new MonitorActionResult { Success = true, Message = "Monitor already stopped (info file missing)." };
            }

            var stopped = await this._monitorLifecycleService.StopAgentAsync().ConfigureAwait(false);
            if (stopped)
            {
                return new MonitorActionResult { Success = true, Message = $"Monitor stopped on port {status.Port}." };
            }

            this._logger.LogWarning("Monitor stop request failed.");
            return new MonitorActionResult { Success = false, Message = "Failed to stop monitor." };
        }
    }
}
