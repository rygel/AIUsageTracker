// <copyright file="MonitorStatusResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services;

public readonly record struct MonitorStatusResult(
    bool IsRunning,
    int Port,
    string Message,
    string? Error,
    string? ServiceHealth,
    string? LastRefreshError,
    int ProvidersInBackoff,
    IReadOnlyList<string> FailingProviders,
    bool? IsContractCompatible,
    string? ContractVersion,
    string? MinClientContractVersion,
    string? ContractMessage,
    string? StartupState,
    string? StartupFailureReason);
