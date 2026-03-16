// <copyright file="ProviderStatusPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderStatusPresentation(
    bool UseHorizontalLayout,
    string PrimaryText,
    string PrimaryResourceKey,
    bool PrimaryItalic,
    ReadOnlyCollection<ProviderStatusLine> SecondaryLines);
