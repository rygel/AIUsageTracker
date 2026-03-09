// <copyright file="ProviderInfo.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models
{
    public class ProviderInfo
    {
        public string ProviderId { get; set; } = string.Empty;

        public string ProviderName { get; set; } = string.Empty;

        public string PlanType { get; set; } = string.Empty;

        public bool IsActive { get; set; }

        public string? AuthSource { get; set; }

        public string? AccountName { get; set; }

        public double LatestUsage { get; set; }

        public DateTime? NextResetTime { get; set; }
    }

}
