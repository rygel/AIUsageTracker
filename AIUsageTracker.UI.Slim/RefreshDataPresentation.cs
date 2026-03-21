// <copyright file="RefreshDataPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record RefreshDataPresentation(
    bool ApplyLatestUsages,
    bool UpdateLastMonitorTimestamp,
    string? StatusMessage,
    StatusType? StatusType,
    bool TriggerTrayIconUpdate,
    bool UseErrorState,
    string? ErrorStateMessage);
