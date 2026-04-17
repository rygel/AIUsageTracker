// <copyright file="IProviderService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

/// <summary>
/// Contract for a provider that fetches AI usage data.
/// </summary>
/// <remarks>
/// <para>
/// Implementations MUST always return <see cref="ProviderUsage"/> rows — never throw to callers.
/// Upstream HTTP or transport failures should be translated into unavailable rows using
/// <c>ProviderBase.CreateUnavailableUsage</c> or equivalent. The monitor layer relies on
/// this guarantee to persist state and make resilience decisions without catching provider exceptions.
/// </para>
/// <para>
/// When a failure has structured context (e.g. from <c>HttpFailureMapper</c>), implementations
/// SHOULD attach it via <see cref="ProviderUsage.FailureContext"/> so that the monitor resilience
/// layer can make more precise backoff and circuit-breaker decisions in the future.
/// This is optional today — monitor logic currently reads only <see cref="ProviderUsage.State"/>,
/// <see cref="ProviderUsage.HttpStatus"/>, and <see cref="ProviderUsage.IsAvailable"/>.
/// </para>
/// </remarks>
public interface IProviderService
{
    string ProviderId { get; }

    ProviderDefinition Definition { get; }

    bool CanHandleProviderId(string providerId)
    {
        return this.Definition.HandlesProviderId(providerId);
    }

    /// <summary>
    /// Fetches current usage data for this provider.
    /// Always returns at least one <see cref="ProviderUsage"/> row.
    /// On failure, returns an unavailable row — does not throw.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default);
}
