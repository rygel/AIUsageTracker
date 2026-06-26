// <copyright file="HttpStatusHistoryPoint.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public class HttpStatusHistoryPoint
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public int SuccessCount { get; set; }

    public int ErrorCount { get; set; }

    public int TotalCount { get; set; }
}
