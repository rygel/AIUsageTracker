// <copyright file="ScenarioMonitorLauncherClient.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Web.Services;

internal sealed class ScenarioMonitorLauncherClient : IMonitorLauncherClient
{
    internal const string ScenarioPathEnvironmentVariable = "AIUSAGETRACKER_WEB_TEST_MONITOR_SCENARIO";

    private readonly object _syncRoot = new();
    private readonly IReadOnlyList<ScenarioStatus> _statusSequence;
    private readonly bool _ensureAgentRunningResult;
    private readonly bool _stopAgentResult;
    private int _statusIndex;

    public ScenarioMonitorLauncherClient(string scenarioPath)
    {
        if (string.IsNullOrWhiteSpace(scenarioPath))
        {
            throw new ArgumentException("Scenario path is required.", nameof(scenarioPath));
        }

        var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(
            File.ReadAllText(scenarioPath),
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            })
            ?? throw new InvalidOperationException($"Monitor launcher scenario '{scenarioPath}' was empty.");
        if (scenario.StatusSequence.Count == 0)
        {
            throw new InvalidOperationException($"Monitor launcher scenario '{scenarioPath}' did not define any status entries.");
        }

        this._statusSequence = scenario.StatusSequence;
        this._ensureAgentRunningResult = scenario.EnsureAgentRunningResult;
        this._stopAgentResult = scenario.StopAgentResult;
    }

    public Task<MonitorAgentStatus> GetAgentStatusInfoAsync()
    {
        lock (this._syncRoot)
        {
            var index = Math.Min(this._statusIndex, this._statusSequence.Count - 1);
            var status = this._statusSequence[index];
            this._statusIndex++;
            return Task.FromResult(new MonitorAgentStatus
            {
                IsRunning = status.IsRunning,
                Port = status.Port,
                HasMetadata = status.HasMetadata,
                Message = status.Message,
                Error = status.Error,
            });
        }
    }

    public Task<bool> EnsureAgentRunningAsync()
    {
        return Task.FromResult(this._ensureAgentRunningResult);
    }

    public Task<bool> StopAgentAsync()
    {
        return Task.FromResult(this._stopAgentResult);
    }

    internal sealed class ScenarioDefinition
    {
        public List<ScenarioStatus> StatusSequence { get; set; } = new();

        public bool EnsureAgentRunningResult { get; set; }

        public bool StopAgentResult { get; set; }
    }

    internal sealed class ScenarioStatus
    {
        public bool IsRunning { get; set; }

        public int Port { get; set; }

        public bool HasMetadata { get; set; }

        public string Message { get; set; } = string.Empty;

        public string? Error { get; set; }
    }
}
