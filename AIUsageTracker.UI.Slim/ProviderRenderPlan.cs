// <copyright file="ProviderRenderPlan.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderRenderPlan(
    int RawCount,
    int RenderedCount,
    string? Message,
    IReadOnlyList<ProviderSectionLayout> Sections);
