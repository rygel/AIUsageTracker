// <copyright file="ResetEvent.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models
{
    public class ResetEvent
    {
        public string Id { get; set; } = string.Empty;

        public string ProviderId { get; set; } = string.Empty;

        public string ProviderName { get; set; } = string.Empty;

        public double? PreviousUsage { get; set; }

        public double? NewUsage { get; set; }

        public string ResetType { get; set; } = string.Empty;

        public string Timestamp { get; set; } = string.Empty;
    }
}
