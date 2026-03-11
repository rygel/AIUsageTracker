// <copyright file="ProviderUsageDetail.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public class ProviderUsageDetail
{
    public string Name { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string GroupName { get; set; } = string.Empty;

    public string Used { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime? NextResetTime { get; set; }

    public ProviderUsageDetailType DetailType { get; set; } = ProviderUsageDetailType.Unknown;

    public WindowKind WindowKind { get; set; } = WindowKind.None;

    public bool IsPrimaryQuotaDetail()
    {
        return this.DetailType == ProviderUsageDetailType.QuotaWindow && this.WindowKind == WindowKind.Primary;
    }

    public bool IsSecondaryQuotaDetail()
    {
        return this.DetailType == ProviderUsageDetailType.QuotaWindow && this.WindowKind == WindowKind.Secondary;
    }

    public bool IsWindowQuotaDetail()
    {
        return this.DetailType == ProviderUsageDetailType.QuotaWindow;
    }

    public bool IsCreditDetail()
    {
        return this.DetailType == ProviderUsageDetailType.Credit;
    }

    public bool IsDisplayableSubProviderDetail()
    {
        return this.DetailType == ProviderUsageDetailType.Model || this.DetailType == ProviderUsageDetailType.Other;
    }
}
