// <copyright file="UsageSummary.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models
{
    public class UsageSummary
    {
        public int ProviderCount { get; set; }

        public double AverageUsage { get; set; }

        public string? LastUpdate { get; set; }
    }
}
