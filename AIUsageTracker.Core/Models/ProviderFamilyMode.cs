// <copyright file="ProviderFamilyMode.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public enum ProviderFamilyMode
{
    Standalone = 0,
    VisibleDerivedProviders = 1,
    DynamicChildProviderRows = 4,

    /// <summary>
    /// Each quota window card is expanded as an independent top-level card.
    /// No parent aggregate card; no child rows. Used by Claude Code.
    /// </summary>
    FlatWindowCards = 5,
}
