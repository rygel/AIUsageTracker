// <copyright file="ProviderDerivedModelAssignment.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Infrastructure.Providers;

public sealed record ProviderDerivedModelAssignment(
    string ProviderId,
    AgentGroupedModelUsage Model);
