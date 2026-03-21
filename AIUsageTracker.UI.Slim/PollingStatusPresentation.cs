// <copyright file="PollingStatusPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record PollingStatusPresentation(
    string? Message,
    StatusType? StatusType,
    bool SwitchToStartupInterval);
