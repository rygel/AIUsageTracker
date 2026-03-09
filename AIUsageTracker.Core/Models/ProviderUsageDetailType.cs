// <copyright file="ProviderUsageDetailType.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models
{
    public enum ProviderUsageDetailType
    {
        Unknown = 0,
        QuotaWindow = 1,
        Credit = 2,
        Model = 3,
        Other = 4,
    }
}
