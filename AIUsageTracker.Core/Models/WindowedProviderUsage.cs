// <copyright file="WindowedProviderUsage.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

public sealed class WindowedProviderUsage : QuotaProviderUsage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentProviderId { get; set; }

    [JsonIgnore]
    public IReadOnlyList<QuotaProviderUsage>? WindowCards { get; set; }
}
