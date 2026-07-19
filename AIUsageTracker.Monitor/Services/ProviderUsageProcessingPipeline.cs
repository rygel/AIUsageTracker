// <copyright file="ProviderUsageProcessingPipeline.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.Monitor.Services;

public class ProviderUsageProcessingPipeline : IProviderUsageProcessingPipeline
{
    private readonly ILogger<ProviderUsageProcessingPipeline> _logger;
    private long _totalProcessedEntries;
    private long _totalAcceptedEntries;
    private long _totalRejectedEntries;
    private long _invalidIdentityCount;
    private long _inactiveProviderFilteredCount;
    private long _placeholderFilteredCount;
    private long _normalizedCount;
    private long _privacyRedactedCount;
    private long _lastProcessedAtUtcTicks;
    private int _lastRunTotalEntries;
    private int _lastRunAcceptedEntries;

    public ProviderUsageProcessingPipeline(ILogger<ProviderUsageProcessingPipeline> logger)
    {
        this._logger = logger;
    }

    public ProviderUsageProcessingResult Process(
        IEnumerable<ProviderUsage> usages,
        IReadOnlyCollection<string> activeProviderIds,
        bool isPrivacyMode)
    {
        ArgumentNullException.ThrowIfNull(usages);
        ArgumentNullException.ThrowIfNull(activeProviderIds);

        var accepted = new List<ProviderUsage>();
        var activeSet = BuildActiveProviderSet(activeProviderIds);

        var invalidIdentityCount = 0;
        var inactiveProviderFilteredCount = 0;
        var placeholderFilteredCount = 0;
        var normalizedCount = 0;
        var privacyRedactedCount = 0;
        var totalProcessedEntries = 0;

        foreach (var usage in usages)
        {
            totalProcessedEntries++;

            // Placeholder check must run on the ORIGINAL usage before normalization,
            // because NormalizeUsage fills in "Unavailable" for empty descriptions —
            // which would prevent the filter from ever seeing a blank description.
            if (ShouldRejectPlaceholderStage(usage, ref placeholderFilteredCount))
            {
                continue;
            }

            if (!this.TryNormalizeUsageForProcessing(
                    usage,
                    isPrivacyMode,
                    ref invalidIdentityCount,
                    ref normalizedCount,
                    ref privacyRedactedCount,
                    out var normalized))
            {
                continue;
            }

            if (!PassesAuthorityStage(
                    activeSet,
                    normalized.ProviderId,
                    ref inactiveProviderFilteredCount))
            {
                continue;
            }

            accepted.Add(normalized);
        }

        NormalizeFamilyAccountIdentity(accepted);

        this.RecordSnapshot(
            totalProcessedEntries,
            accepted.Count,
            invalidIdentityCount,
            inactiveProviderFilteredCount,
            placeholderFilteredCount,
            normalizedCount,
            privacyRedactedCount);

        return new ProviderUsageProcessingResult
        {
            Usages = accepted,
            InvalidIdentityCount = invalidIdentityCount,
            InactiveProviderFilteredCount = inactiveProviderFilteredCount,
            PlaceholderFilteredCount = placeholderFilteredCount,
            DetailContractAdjustedCount = 0,
            NormalizedCount = normalizedCount,
            PrivacyRedactedCount = privacyRedactedCount,
        };
    }

    public ProviderUsageProcessingTelemetrySnapshot GetSnapshot()
    {
        var lastProcessedTicks = Interlocked.Read(ref this._lastProcessedAtUtcTicks);
        var lastProcessedAtUtc = lastProcessedTicks > 0
            ? (DateTime?)new DateTime(lastProcessedTicks, DateTimeKind.Utc)
            : null;

        return new ProviderUsageProcessingTelemetrySnapshot
        {
            TotalProcessedEntries = Interlocked.Read(ref this._totalProcessedEntries),
            TotalAcceptedEntries = Interlocked.Read(ref this._totalAcceptedEntries),
            TotalRejectedEntries = Interlocked.Read(ref this._totalRejectedEntries),
            InvalidIdentityCount = Interlocked.Read(ref this._invalidIdentityCount),
            InactiveProviderFilteredCount = Interlocked.Read(ref this._inactiveProviderFilteredCount),
            PlaceholderFilteredCount = Interlocked.Read(ref this._placeholderFilteredCount),
            DetailContractAdjustedCount = 0,
            NormalizedCount = Interlocked.Read(ref this._normalizedCount),
            PrivacyRedactedCount = Interlocked.Read(ref this._privacyRedactedCount),
            LastProcessedAtUtc = lastProcessedAtUtc,
            LastRunTotalEntries = Volatile.Read(ref this._lastRunTotalEntries),
            LastRunAcceptedEntries = Volatile.Read(ref this._lastRunAcceptedEntries),
        };
    }

    private static HashSet<string> BuildActiveProviderSet(IReadOnlyCollection<string> activeProviderIds)
    {
        return activeProviderIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private bool TryNormalizeUsageForProcessing(
        ProviderUsage usage,
        bool isPrivacyMode,
        ref int invalidIdentityCount,
        ref int normalizedCount,
        ref int privacyRedactedCount,
        out ProviderUsage normalized)
    {
        normalized = this.NormalizeUsage(
            usage,
            isPrivacyMode,
            ref normalizedCount,
            ref privacyRedactedCount);

        if (!string.IsNullOrWhiteSpace(normalized.ProviderId))
        {
            return true;
        }

        invalidIdentityCount++;
        this._logger.LogWarning("Rejecting usage entry with empty provider id.");
        return false;
    }

    private static bool PassesAuthorityStage(
        HashSet<string> activeProviderIds,
        string usageProviderId,
        ref int inactiveProviderFilteredCount)
    {
        if (IsUsageForAnyActiveProvider(activeProviderIds, usageProviderId))
        {
            return true;
        }

        inactiveProviderFilteredCount++;
        return false;
    }

    private static bool ShouldRejectPlaceholderStage(
        ProviderUsage usage,
        ref int placeholderFilteredCount)
    {
        if (!IsPlaceholderUnavailableUsage(usage))
        {
            return false;
        }

        placeholderFilteredCount++;
        return true;
    }

    private void RecordSnapshot(
        int totalProcessedEntries,
        int acceptedEntries,
        int invalidIdentityCount,
        int inactiveProviderFilteredCount,
        int placeholderFilteredCount,
        int normalizedCount,
        int privacyRedactedCount)
    {
        var rejectedEntries = totalProcessedEntries - acceptedEntries;

        Interlocked.Add(ref this._totalProcessedEntries, totalProcessedEntries);
        Interlocked.Add(ref this._totalAcceptedEntries, acceptedEntries);
        Interlocked.Add(ref this._totalRejectedEntries, rejectedEntries);
        Interlocked.Add(ref this._invalidIdentityCount, invalidIdentityCount);
        Interlocked.Add(ref this._inactiveProviderFilteredCount, inactiveProviderFilteredCount);
        Interlocked.Add(ref this._placeholderFilteredCount, placeholderFilteredCount);
        Interlocked.Add(ref this._normalizedCount, normalizedCount);
        Interlocked.Add(ref this._privacyRedactedCount, privacyRedactedCount);

        Volatile.Write(ref this._lastRunTotalEntries, totalProcessedEntries);
        Volatile.Write(ref this._lastRunAcceptedEntries, acceptedEntries);
        Interlocked.Exchange(ref this._lastProcessedAtUtcTicks, DateTime.UtcNow.Ticks);
    }

    private ProviderUsage NormalizeUsage(
        ProviderUsage usage,
        bool isPrivacyMode,
        ref int normalizedCount,
        ref int privacyRedactedCount)
    {
        var srcQuota = usage as QuotaProviderUsage;

        var origProviderId = usage.ProviderId;
        var origProviderName = usage.ProviderName;
        var origDescription = usage.Description;
        var origFetchedAt = usage.FetchedAt;
        var origHttpStatus = usage.HttpStatus;
        var origResponseLatencyMs = usage.ResponseLatencyMs;
        var origUpstreamResponseValidity = usage.UpstreamResponseValidity;
        var origUpstreamResponseNote = usage.UpstreamResponseNote;
        var origNextResetTime = srcQuota?.NextResetTime;
        var origRequestsUsed = srcQuota?.RequestsUsed ?? 0;
        var origRequestsAvailable = srcQuota?.RequestsAvailable ?? 0;
        var origUsedPercent = srcQuota?.UsedPercent ?? 0;

        usage.ProviderId = usage.ProviderId?.Trim() ?? string.Empty;
        usage.ProviderName = string.IsNullOrWhiteSpace(usage.ProviderName)
            ? usage.ProviderId
            : usage.ProviderName.Trim();
        var definition = ProviderMetadataCatalog.Find(usage.ProviderId);

        if (srcQuota != null)
        {
            srcQuota.RequestsUsed = SanitizeNonNegativeFinite(srcQuota.RequestsUsed);
            srcQuota.RequestsAvailable = SanitizeNonNegativeFinite(srcQuota.RequestsAvailable);
            srcQuota.UsedPercent = NormalizePercentage(srcQuota, srcQuota.RequestsUsed, srcQuota.RequestsAvailable);
            srcQuota.PlanType = definition?.PlanType ?? srcQuota.PlanType;
            srcQuota.IsQuotaBased = definition?.IsQuotaBased ?? srcQuota.IsQuotaBased;
            srcQuota.DisplayAsFraction = srcQuota.DisplayAsFraction || (definition?.DisplayAsFraction ?? false);
            srcQuota.IsStatusOnly = srcQuota.IsStatusOnly || (definition?.IsStatusOnly ?? false);
            srcQuota.IsCurrencyUsage = srcQuota.IsCurrencyUsage || (definition?.IsCurrencyUsage ?? false);
            srcQuota.NextResetTime = srcQuota.NextResetTime?.ToUniversalTime();
        }

        usage.ResponseLatencyMs = SanitizeNonNegativeFinite(usage.ResponseLatencyMs);
        usage.FetchedAt = NormalizeFetchedAt(usage.FetchedAt);
        usage.HttpStatus = usage.HttpStatus is >= 0 and <= 599 ? usage.HttpStatus : 0;
        usage.IsTooltipOnly = usage.IsTooltipOnly || (definition?.IsTooltipOnly ?? false);

        var description = (usage.Description ?? string.Empty).Trim();
        if (!usage.IsAvailable && string.IsNullOrWhiteSpace(description))
        {
            description = "Unavailable";
        }

        usage.Description = description;

        if (isPrivacyMode)
        {
            if (!string.IsNullOrWhiteSpace(usage.RawJson) ||
                !string.IsNullOrWhiteSpace(usage.AccountName) ||
                !string.IsNullOrWhiteSpace(usage.ConfigKey))
            {
                privacyRedactedCount++;
            }

            usage.RawJson = null;
            usage.AccountName = string.Empty;
            usage.ConfigKey = string.Empty;
        }

        var upstreamEval = usage.EvaluateUpstreamResponseValidity();
        usage.UpstreamResponseValidity = upstreamEval.Validity;
        usage.UpstreamResponseNote = upstreamEval.Note;

        if (!StringEquals(origProviderId, usage.ProviderId) ||
            !StringEquals(origProviderName, usage.ProviderName) ||
            Math.Abs(origResponseLatencyMs - usage.ResponseLatencyMs) > 0.001 ||
            origFetchedAt != usage.FetchedAt ||
            !StringEquals(origDescription, usage.Description) ||
            origHttpStatus != usage.HttpStatus ||
            origUpstreamResponseValidity != usage.UpstreamResponseValidity ||
            !StringEquals(origUpstreamResponseNote, usage.UpstreamResponseNote) ||
            (srcQuota != null && (
                Math.Abs(origRequestsUsed - srcQuota.RequestsUsed) > 0.001 ||
                Math.Abs(origRequestsAvailable - srcQuota.RequestsAvailable) > 0.001 ||
                Math.Abs(origUsedPercent - srcQuota.UsedPercent) > 0.001 ||
                origNextResetTime != srcQuota.NextResetTime)))
        {
            normalizedCount++;
        }

        return usage;
    }

    private static bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private static DateTime NormalizeFetchedAt(DateTime fetchedAt)
    {
        if (fetchedAt == default)
        {
            return DateTime.UtcNow;
        }

        return fetchedAt.Kind switch
        {
            DateTimeKind.Utc => fetchedAt,
            DateTimeKind.Local => fetchedAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(fetchedAt, DateTimeKind.Utc),
        };
    }

    private static double SanitizeNonNegativeFinite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return 0;
        }

        return value;
    }

    private static double NormalizePercentage(QuotaProviderUsage usage, double requestsUsed, double requestsAvailable)
    {
        var original = usage.UsedPercent;
        var isFinite = !double.IsNaN(original) && !double.IsInfinity(original);
        var isInRange = original is >= 0 and <= 100;

        if (isFinite && isInRange)
        {
            return original;
        }

        return UsageMath.CalculateUsedPercent(requestsUsed, requestsAvailable);
    }

    private static bool IsUsageForAnyActiveProvider(HashSet<string> activeProviderIds, string usageProviderId)
    {
        return activeProviderIds.Any(providerId =>
            (ProviderMetadataCatalog.Find(providerId)?.HandlesProviderId(usageProviderId) ?? false));
    }

    private static bool IsPlaceholderUnavailableUsage(ProviderUsage usage)
    {
        var q = usage as QuotaProviderUsage;
        if ((q?.RequestsAvailable ?? 0) is not 0 ||
            (q?.RequestsUsed ?? 0) is not 0 ||
            usage.IsAvailable)
        {
            return false;
        }

        // Keep unavailable entries that carry a description — they are actionable
        // (e.g. "Codex auth token not found", "API Key missing").  Only discard
        // truly empty entries that have nothing to show the user.
        return string.IsNullOrWhiteSpace(usage.Description);
    }

    private static void NormalizeFamilyAccountIdentity(List<ProviderUsage> usages)
    {
        foreach (var group in usages.GroupBy(
                     usage => ProviderMetadataCatalog.GetProviderOwnerId(usage.ProviderId ?? string.Empty),
                     StringComparer.OrdinalIgnoreCase))
        {
            var resolvedAccountName = group
                .Select(usage => usage.AccountName?.Trim())
                .FirstOrDefault(accountName => !string.IsNullOrWhiteSpace(accountName));

            if (string.IsNullOrWhiteSpace(resolvedAccountName))
            {
                continue;
            }

            foreach (var usage in group.Where(u => string.IsNullOrWhiteSpace(u.AccountName)))
            {
                usage.AccountName = resolvedAccountName;
            }
        }
    }
}
