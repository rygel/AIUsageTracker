// <copyright file="SettingsWindowDeterministicFixtureData.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed class SettingsWindowDeterministicFixtureData
{
    public required List<ProviderConfig> Configs { get; init; }

    public required List<ProviderUsage> Usages { get; init; }

    public required IReadOnlyList<SettingsWindowHistoryRow> HistoryRows { get; init; }

    public string MonitorStatusText { get; init; } = "Running";

    public string MonitorPortText { get; init; } = "5000";

    public string MonitorLogsText { get; init; } =
        "Monitor health check: OK" + Environment.NewLine +
        "Diagnostics available in Settings > Monitor.";
}
