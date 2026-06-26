// <copyright file="SpendingTrendPoint.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public class SpendingTrendPoint
{
    public string ProviderId { get; set; } = string.Empty;

    public string ProviderName { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    public double Amount { get; set; }
}
