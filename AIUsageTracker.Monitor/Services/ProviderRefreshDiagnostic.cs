// <copyright file="ProviderRefreshDiagnostic.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderRefreshDiagnostic
{
    public string ProviderId { get; init; } = string.Empty;

    public DateTime? LastRefreshAttemptUtc { get; init; }

    public DateTime? LastSuccessfulRefreshUtc { get; init; }

    public string? LastRefreshError { get; init; }

    public bool IsCircuitOpen { get; init; }

    public DateTime? CircuitOpenUntilUtc { get; init; }

    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// Gets the classification of the last recorded failure, when structured failure context
    /// was attached by the provider. Null when the provider has not yet adopted FailureContext
    /// or when the last refresh was successful.
    /// </summary>
    public HttpFailureClassification? LastFailureClassification { get; init; }
}
