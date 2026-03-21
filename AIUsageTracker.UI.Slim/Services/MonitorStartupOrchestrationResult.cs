// <copyright file="MonitorStartupOrchestrationResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim.Services;

public sealed record MonitorStartupOrchestrationResult(bool IsSuccess, bool IsLaunchFailure);
