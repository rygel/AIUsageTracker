// <copyright file="ProviderAuthIdentities.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderAuthIdentities(
    string? GitHubUsername,
    string? OpenAiUsername,
    string? CodexUsername,
    string? AntigravityUsername = null);
