// <copyright file="ProviderRefreshCircuitBreakerService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Monitor.Services;

public class ProviderRefreshCircuitBreakerService
{
    private const int CircuitBreakerFailureThreshold = 3;
    private static readonly TimeSpan CircuitBreakerBaseBackoff = TimeSpan.FromMinutes(1);

    // Maximum backoff before the circuit retries. UsageDatabase.StaleDataThreshold is set to
    // 2× this value so that a row is only flagged stale after at least two max-backoff windows
    // have passed without a successful refresh. If you change this value, update StaleDataThreshold too.
    private static readonly TimeSpan CircuitBreakerMaxBackoff = TimeSpan.FromMinutes(30);
    private readonly ILogger<ProviderRefreshCircuitBreakerService> _logger;
    private readonly object _providerFailureLock = new();
    private readonly Dictionary<string, ProviderFailureState> _providerFailureStates = new(StringComparer.OrdinalIgnoreCase);

    public ProviderRefreshCircuitBreakerService(ILogger<ProviderRefreshCircuitBreakerService> logger)
    {
        this._logger = logger;
    }

    public IList<ProviderConfig> GetRefreshableConfigs(IList<ProviderConfig> activeConfigs, bool forceAll)
    {
        ArgumentNullException.ThrowIfNull(activeConfigs);

        if (forceAll || activeConfigs.Count == 0)
        {
            return activeConfigs;
        }

        var now = DateTime.UtcNow;
        var refreshable = new List<ProviderConfig>(activeConfigs.Count);

        lock (this._providerFailureLock)
        {
            foreach (var config in activeConfigs)
            {
                if (!this._providerFailureStates.TryGetValue(config.ProviderId, out var state))
                {
                    refreshable.Add(config);
                    continue;
                }

                if (state.CircuitOpenUntilUtc.HasValue && state.CircuitOpenUntilUtc.Value > now)
                {
                    this._logger.LogDebug(
                        "Circuit open for {ProviderId}; skipping until {RetryUtc:HH:mm:ss} UTC",
                        config.ProviderId,
                        state.CircuitOpenUntilUtc.Value);
                    continue;
                }

                state.CircuitOpenUntilUtc = null;
                refreshable.Add(config);
            }
        }

        return refreshable;
    }

    public void UpdateProviderFailureStates(IList<ProviderConfig> queriedConfigs, IReadOnlyCollection<ProviderUsage> usages)
    {
        ArgumentNullException.ThrowIfNull(queriedConfigs);

        if (queriedConfigs.Count == 0)
        {
            return;
        }

        lock (this._providerFailureLock)
        {
            var now = DateTime.UtcNow;
            foreach (var config in queriedConfigs)
            {
                if (!this._providerFailureStates.TryGetValue(config.ProviderId, out var state))
                {
                    state = new ProviderFailureState();
                    this._providerFailureStates[config.ProviderId] = state;
                }

                state.LastRefreshAttemptUtc = now;
                var providerUsages = usages
                    .Where(u => ProviderMetadataCatalog.Find(config.ProviderId)?.HandlesProviderId(u.ProviderId) ?? false)
                    .ToList();
                var isSuccess = providerUsages.Any(IsSuccessfulUsage);

                if (isSuccess)
                {
                    var hadFailures = state.ConsecutiveFailures > 0 ||
                        state.CircuitOpenUntilUtc.HasValue ||
                        !string.IsNullOrWhiteSpace(state.LastError);
                    state.ConsecutiveFailures = 0;
                    state.CircuitOpenUntilUtc = null;
                    state.LastError = null;
                    state.LastFailureContext = null;
                    state.LastSuccessfulRefreshUtc = now;

                    if (hadFailures)
                    {
                        this._logger.LogDebug("Circuit reset for {ProviderId}", config.ProviderId);
                    }

                    continue;
                }

                state.ConsecutiveFailures++;
                state.LastError = GetFailureMessage(providerUsages);
                state.LastFailureContext = providerUsages
                    .Select(u => u.FailureContext)
                    .FirstOrDefault(fc => fc != null);

                if (state.ConsecutiveFailures >= CircuitBreakerFailureThreshold)
                {
                    var backoffDelay = GetCircuitBreakerDelay(state.ConsecutiveFailures, state.LastFailureContext);
                    state.CircuitOpenUntilUtc = now.Add(backoffDelay);

                    this._logger.LogWarning(
                        "Circuit opened for {ProviderId} after {Failures} failures; retry at {RetryUtc:HH:mm:ss} UTC ({DelayMinutes:F1} min). Last error: {Error}",
                        config.ProviderId,
                        state.ConsecutiveFailures,
                        state.CircuitOpenUntilUtc.Value,
                        backoffDelay.TotalMinutes,
                        state.LastError);
                }
                else
                {
                    this._logger.LogDebug(
                        "Provider {ProviderId} failure {Failures}/{Threshold}: {Error}",
                        config.ProviderId,
                        state.ConsecutiveFailures,
                        CircuitBreakerFailureThreshold,
                        state.LastError);
                }
            }
        }
    }

    /// <summary>
    /// Creates synthetic <see cref="ProviderUsage"/> entries for providers whose circuit is
    /// currently open so they are stored in the database and surfaced in the UI rather than
    /// showing stale data silently.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<ProviderUsage> CreateCircuitOpenUsages(IEnumerable<ProviderConfig> skippedConfigs)
    {
        ArgumentNullException.ThrowIfNull(skippedConfigs);

        var result = new List<ProviderUsage>();
        lock (this._providerFailureLock)
        {
            var now = DateTime.UtcNow;
            foreach (var config in skippedConfigs)
            {
                if (!this._providerFailureStates.TryGetValue(config.ProviderId, out var state) ||
                    !state.CircuitOpenUntilUtc.HasValue ||
                    state.CircuitOpenUntilUtc.Value <= now)
                {
                    continue;
                }

                var retryLocal = state.CircuitOpenUntilUtc.Value.ToLocalTime();
                var description = string.IsNullOrWhiteSpace(state.LastError)
                    ? $"Temporarily paused after repeated failures — next check at {retryLocal:HH:mm}"
                    : $"Temporarily paused — next check at {retryLocal:HH:mm} (last error: {state.LastError})";

                var displayName = ProviderMetadataCatalog.ResolveDisplayLabel(config.ProviderId, config.ProviderId);
                result.Add(new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = displayName,
                    IsAvailable = false,
                    State = ProviderUsageState.Unavailable,
                    Description = description,
                    RequestsUsed = 0,
                    RequestsAvailable = 0,
                    FetchedAt = now,
                    AuthSource = config.AuthSource ?? string.Empty,
                });
            }
        }

        return result;
    }

    public IReadOnlyList<ProviderRefreshDiagnostic> GetProviderDiagnostics()
    {
        lock (this._providerFailureLock)
        {
            var now = DateTime.UtcNow;
            return this._providerFailureStates
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new ProviderRefreshDiagnostic
                {
                    ProviderId = entry.Key,
                    LastRefreshAttemptUtc = entry.Value.LastRefreshAttemptUtc,
                    LastSuccessfulRefreshUtc = entry.Value.LastSuccessfulRefreshUtc,
                    LastRefreshError = entry.Value.LastError,
                    IsCircuitOpen = entry.Value.CircuitOpenUntilUtc.HasValue && entry.Value.CircuitOpenUntilUtc.Value > now,
                    CircuitOpenUntilUtc = entry.Value.CircuitOpenUntilUtc,
                    ConsecutiveFailures = entry.Value.ConsecutiveFailures,
                    LastFailureClassification = entry.Value.LastFailureContext?.Classification,
                })
                .ToArray();
        }
    }

    public void ResetProvider(string providerId, string reason)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return;
        }

        lock (this._providerFailureLock)
        {
            if (!this._providerFailureStates.TryGetValue(providerId, out var state))
            {
                return;
            }

            var hadState = state.ConsecutiveFailures > 0 ||
                state.CircuitOpenUntilUtc.HasValue ||
                !string.IsNullOrWhiteSpace(state.LastError);
            if (!hadState)
            {
                return;
            }

            state.ConsecutiveFailures = 0;
            state.CircuitOpenUntilUtc = null;
            state.LastError = null;
            this._logger.LogInformation(
                "Circuit reset for {ProviderId} due to {Reason}",
                providerId,
                reason);
        }
    }

    private static bool IsSuccessfulUsage(ProviderUsage usage)
    {
        if (!usage.IsAvailable)
        {
            return false;
        }

        if (usage.HttpStatus >= 400)
        {
            return false;
        }

        if (usage.State == ProviderUsageState.Error)
        {
            return false;
        }

        return true;
    }

    private static string GetFailureMessage(IReadOnlyCollection<ProviderUsage> providerUsages)
    {
        if (providerUsages.Count == 0)
        {
            return "No usage data returned";
        }

        var failedUsage = providerUsages.FirstOrDefault(u => !IsSuccessfulUsage(u));
        if (failedUsage != null && !string.IsNullOrWhiteSpace(failedUsage.Description))
        {
            return failedUsage.Description;
        }

        return "Provider returned no successful usage entries";
    }

    private static TimeSpan GetCircuitBreakerDelay(int consecutiveFailures, HttpFailureContext? failureContext = null)
    {
        if (failureContext?.Classification == HttpFailureClassification.RateLimit)
        {
            // Honour server-supplied retry delay when available, capped at max backoff.
            if (failureContext.RetryAfter.HasValue)
            {
                return failureContext.RetryAfter.Value < CircuitBreakerMaxBackoff
                    ? failureContext.RetryAfter.Value
                    : CircuitBreakerMaxBackoff;
            }

            // Rate limit without explicit RetryAfter: use base backoff — no exponential growth
            // because the failure type is quota-based, not a sign of general provider instability.
            return CircuitBreakerBaseBackoff;
        }

        var backoffLevel = Math.Max(0, consecutiveFailures - CircuitBreakerFailureThreshold);
        var exponent = Math.Min(backoffLevel, 6);
        var seconds = CircuitBreakerBaseBackoff.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, CircuitBreakerMaxBackoff.TotalSeconds));
    }

    private sealed class ProviderFailureState
    {
        public int ConsecutiveFailures { get; set; }

        public DateTime? CircuitOpenUntilUtc { get; set; }

        public DateTime? LastRefreshAttemptUtc { get; set; }

        public DateTime? LastSuccessfulRefreshUtc { get; set; }

        public string? LastError { get; set; }

        /// <summary>
        /// Gets or sets the structured failure context from the most recent failed refresh,
        /// when the provider attached one. Null for providers not yet adopting FailureContext.
        /// Used to select classification-specific backoff policies.
        /// </summary>
        public HttpFailureContext? LastFailureContext { get; set; }
    }
}
