// <copyright file="ErrorStatePresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ErrorStatePresentation(
    bool ReplaceProviderCards,
    StatusType StatusType);
