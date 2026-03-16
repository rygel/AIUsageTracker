// <copyright file="ProviderRenderPreparation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderRenderPreparation(
    IReadOnlyList<ProviderUsage> DisplayableUsages);
