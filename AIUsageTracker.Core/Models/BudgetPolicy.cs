// <copyright file="BudgetPolicy.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models
{
    public class BudgetPolicy
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string ProviderId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;

        public double Limit { get; set; }

        public string Currency { get; set; } = "USD";

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
