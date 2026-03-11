// <copyright file="ProviderRefreshCircuitBreakerService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public class ProviderRefreshCircuitBreakerService
{
    private const int CircuitBreakerFailureThreshold = 3;
    private static readonly TimeSpan CircuitBreakerBaseBackoff = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CircuitBreakerMaxBackoff = TimeSpan.FromMinutes(30);
    private readonly ILogger<ProviderRefreshCircuitBreakerService> _logger;
    private readonly object _providerFailureLock = new();
    private readonly Dictionary<string, ProviderFailureState> _providerFailureStates = new(StringComparer.OrdinalIgnoreCase);

    private sealed class ProviderFailureState
    {
        public int ConsecutiveFailures { get; set; }

        public DateTime? CircuitOpenUntilUtc { get; set; }

        public DateTime? LastRefreshAttemptUtc { get; set; }

        public DateTime? LastSuccessfulRefreshUtc { get; set; }

        public string? LastError { get; set; }
    }

    public ProviderRefreshCircuitBreakerService(ILogger<ProviderRefreshCircuitBreakerService> logger)
    {
        this._logger = logger;
    }

    public List<ProviderConfig> GetRefreshableConfigs(List<ProviderConfig> activeConfigs, bool forceAll)
    {
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

    public void UpdateProviderFailureStates(IReadOnlyCollection<ProviderConfig> queriedConfigs, IReadOnlyCollection<ProviderUsage> usages)
    {
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
                    .Where(u => IsUsageForProvider(config.ProviderId, u.ProviderId))
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
                    state.LastSuccessfulRefreshUtc = now;

                    if (hadFailures)
                    {
                        this._logger.LogDebug("Circuit reset for {ProviderId}", config.ProviderId);
                    }

                    continue;
                }

                state.ConsecutiveFailures++;
                state.LastError = GetFailureMessage(providerUsages);

                if (state.ConsecutiveFailures >= CircuitBreakerFailureThreshold)
                {
                    var backoffDelay = GetCircuitBreakerDelay(state.ConsecutiveFailures);
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

    private static bool IsUsageForProvider(string providerId, string usageProviderId)
    {
        if (usageProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return usageProviderId.StartsWith($"{providerId}.", StringComparison.OrdinalIgnoreCase);
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

        return string.IsNullOrWhiteSpace(usage.Description) ||
               !usage.Description.StartsWith("[Error]", StringComparison.OrdinalIgnoreCase);
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

    private static TimeSpan GetCircuitBreakerDelay(int consecutiveFailures)
    {
        var backoffLevel = Math.Max(0, consecutiveFailures - CircuitBreakerFailureThreshold);
        var exponent = Math.Min(backoffLevel, 6);
        var seconds = CircuitBreakerBaseBackoff.TotalSeconds * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, CircuitBreakerMaxBackoff.TotalSeconds));
    }
}
