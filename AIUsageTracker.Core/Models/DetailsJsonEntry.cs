// <copyright file="DetailsJsonEntry.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public class DetailsJsonEntry
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string DetailsJson { get; set; } = string.Empty;

    public DateTime FetchedAt { get; set; }
}
