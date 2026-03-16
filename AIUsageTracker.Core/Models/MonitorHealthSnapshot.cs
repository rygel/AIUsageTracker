// <copyright file="MonitorHealthSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.ComponentModel;

namespace AIUsageTracker.Core.Models;

public sealed class MonitorHealthSnapshot
{
    public string Status { get; set; } = "unknown";

    public string ServiceHealth { get; set; } = "unknown";

    public DateTime? Timestamp { get; set; }

    public int Port { get; set; }

    public int ProcessId { get; set; }

    public string? AgentVersion { get; set; }

    public string? ContractVersion { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? ApiContractVersion { get; set; }

    public string? MinClientContractVersion { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public string? MinClientApiContractVersion { get; set; }

    public string? EffectiveContractVersion => this.ContractVersion ?? this.ApiContractVersion;

    public string? EffectiveMinClientContractVersion => this.MinClientContractVersion ?? this.MinClientApiContractVersion;

    public MonitorRefreshHealthSnapshot RefreshHealth { get; set; } = new();
}
