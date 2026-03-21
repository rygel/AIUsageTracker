// <copyright file="MonitorControlPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record MonitorControlPresentation(
    string Message,
    StatusType StatusType,
    bool UpdateToggleButton,
    bool ToggleRunningState,
    bool TriggerRefreshData);
