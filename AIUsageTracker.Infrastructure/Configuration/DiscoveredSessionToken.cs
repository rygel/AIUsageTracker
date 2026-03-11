// <copyright file="DiscoveredSessionToken.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Configuration;

internal sealed record DiscoveredSessionToken(
    string ProviderId,
    string ApiKey,
    string Description,
    string AuthSource);
