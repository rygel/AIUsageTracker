// <copyright file="MonitorRefreshHealthSnapshot.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public sealed class MonitorRefreshHealthSnapshot
{
    public string Status { get; set; } = "unknown";

    public DateTime? LastRefreshAttemptUtc { get; set; }

    public DateTime? LastRefreshCompletedUtc { get; set; }

    public DateTime? LastSuccessfulRefreshUtc { get; set; }

    public string? LastError { get; set; }

    public int ProvidersInBackoff { get; set; }

    public IReadOnlyList<string> FailingProviders { get; set; } = Array.Empty<string>();
}
