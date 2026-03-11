// <copyright file="MonitorActionResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services;

public readonly record struct MonitorActionResult(
    bool Success,
    string Message,
    string? Error,
    string? StartupState,
    string? StartupFailureReason);
