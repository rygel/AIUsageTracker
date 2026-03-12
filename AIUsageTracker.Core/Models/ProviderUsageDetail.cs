// <copyright file="ProviderUsageDetail.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.ComponentModel;
using System.Text.Json.Serialization;

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

    [JsonPropertyName("window_kind")]
    public WindowKind QuotaBucketKind { get; set; } = WindowKind.None;

    [JsonIgnore]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Use QuotaBucketKind.")]
    public WindowKind WindowKind
    {
        get => this.QuotaBucketKind;
        set => this.QuotaBucketKind = value;
    }

    public bool IsPrimaryQuotaDetail()
    {
        return this.IsPrimaryQuotaBucket();
    }

    public bool IsSecondaryQuotaDetail()
    {
        return this.IsSecondaryQuotaBucket();
    }

    public bool IsPrimaryQuotaBucket()
    {
        return this.DetailType == ProviderUsageDetailType.QuotaWindow && this.QuotaBucketKind == WindowKind.Primary;
    }

    public bool IsSecondaryQuotaBucket()
    {
        return this.DetailType == ProviderUsageDetailType.QuotaWindow && this.QuotaBucketKind == WindowKind.Secondary;
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
