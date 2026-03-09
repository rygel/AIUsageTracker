// <copyright file="ProviderStatusLine.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderStatusLine(
    string Text,
    bool Wrap = false,
    bool ExtraTopMargin = false);
