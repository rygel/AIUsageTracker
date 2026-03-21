// <copyright file="MonitorContractPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record MonitorContractPresentation(
    string? WarningMessage,
    bool ShowStatus,
    string? StatusMessage,
    StatusType? StatusType);
