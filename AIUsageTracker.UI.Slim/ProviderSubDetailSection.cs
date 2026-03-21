// <copyright file="ProviderSubDetailSection.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderSubDetailSection(
    string ProviderId,
    string Title,
    IReadOnlyList<ProviderUsageDetail> Details,
    bool IsCollapsed);
