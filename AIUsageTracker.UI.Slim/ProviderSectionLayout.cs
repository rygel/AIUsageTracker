// <copyright file="ProviderSectionLayout.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderSectionLayout(bool IsQuotaBased, IReadOnlyList<ProviderUsage> Usages)
{
    public string Title => this.IsQuotaBased ? "Plans & Quotas" : "Pay As You Go";

    public string SectionKey => this.IsQuotaBased ? "PlansAndQuotas" : "PayAsYouGo";
}
