// <copyright file="ProviderUsageProcessingPipeline.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

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
    private long _detailContractAdjustedCount;
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
        var activeSet = this.BuildActiveProviderSet(activeProviderIds);

        var invalidIdentityCount = 0;
        var inactiveProviderFilteredCount = 0;
        var placeholderFilteredCount = 0;
        var detailContractAdjustedCount = 0;
        var normalizedCount = 0;
        var privacyRedactedCount = 0;
        var totalProcessedEntries = 0;

        foreach (var usage in usages)
        {
            totalProcessedEntries++;
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

            if (!this.PassesAuthorityStage(
                    activeSet,
                    normalized.ProviderId,
                    ref inactiveProviderFilteredCount))
            {
                continue;
            }

            normalized = this.ApplyDetailContractStage(normalized, ref detailContractAdjustedCount);

            if (this.ShouldRejectPlaceholderStage(normalized, ref placeholderFilteredCount))
            {
                continue;
            }

            accepted.Add(normalized);
        }

        this.NormalizeFamilyAccountIdentity(accepted);

        this.RecordSnapshot(
            totalProcessedEntries,
            accepted.Count,
            invalidIdentityCount,
            inactiveProviderFilteredCount,
            placeholderFilteredCount,
            detailContractAdjustedCount,
            normalizedCount,
            privacyRedactedCount);

        return new ProviderUsageProcessingResult
        {
            Usages = accepted,
            InvalidIdentityCount = invalidIdentityCount,
            InactiveProviderFilteredCount = inactiveProviderFilteredCount,
            PlaceholderFilteredCount = placeholderFilteredCount,
            DetailContractAdjustedCount = detailContractAdjustedCount,
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
            DetailContractAdjustedCount = Interlocked.Read(ref this._detailContractAdjustedCount),
            NormalizedCount = Interlocked.Read(ref this._normalizedCount),
            PrivacyRedactedCount = Interlocked.Read(ref this._privacyRedactedCount),
            LastProcessedAtUtc = lastProcessedAtUtc,
            LastRunTotalEntries = Volatile.Read(ref this._lastRunTotalEntries),
            LastRunAcceptedEntries = Volatile.Read(ref this._lastRunAcceptedEntries),
        };
    }

    private static DateTime? InferResetTimeFromDetails(IReadOnlyList<ProviderUsageDetail>? details)
    {
        return UsageMath.InferResetTimeFromDetails(details);
    }

    private HashSet<string> BuildActiveProviderSet(IReadOnlyCollection<string> activeProviderIds)
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

    private bool PassesAuthorityStage(
        HashSet<string> activeProviderIds,
        string usageProviderId,
        ref int inactiveProviderFilteredCount)
    {
        if (this.IsUsageForAnyActiveProvider(activeProviderIds, usageProviderId))
        {
            return true;
        }

        inactiveProviderFilteredCount++;
        return false;
    }

    private ProviderUsage ApplyDetailContractStage(
        ProviderUsage usage,
        ref int detailContractAdjustedCount)
    {
        if (!this.TryCreateDetailContractErrorUsage(usage, out var contractErrorUsage))
        {
            return usage;
        }

        detailContractAdjustedCount++;
        return contractErrorUsage;
    }

    private bool ShouldRejectPlaceholderStage(
        ProviderUsage usage,
        ref int placeholderFilteredCount)
    {
        if (!this.IsPlaceholderUnavailableUsage(usage))
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
        int detailContractAdjustedCount,
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
        Interlocked.Add(ref this._detailContractAdjustedCount, detailContractAdjustedCount);
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
        var providerId = usage.ProviderId?.Trim() ?? string.Empty;
        var providerName = string.IsNullOrWhiteSpace(usage.ProviderName)
            ? providerId
            : usage.ProviderName.Trim();

        var requestsUsed = this.SanitizeNonNegativeFinite(usage.RequestsUsed);
        var requestsAvailable = this.SanitizeNonNegativeFinite(usage.RequestsAvailable);
        var requestsPercentage = this.NormalizePercentage(usage, requestsUsed, requestsAvailable);
        var responseLatencyMs = this.SanitizeNonNegativeFinite(usage.ResponseLatencyMs);
        var fetchedAt = this.NormalizeFetchedAt(usage.FetchedAt);
        var description = (usage.Description ?? string.Empty).Trim();
        if (!usage.IsAvailable && string.IsNullOrWhiteSpace(description))
        {
            description = "Unavailable";
        }

        var httpStatus = usage.HttpStatus is >= 0 and <= 599 ? usage.HttpStatus : 0;

        var details = this.NormalizeDetails(usage.Details);
        var usageNextResetTimeUtc = usage.NextResetTime?.ToUniversalTime();
        var normalizedNextResetTimeUtc = usageNextResetTimeUtc ?? InferResetTimeFromDetails(details);

        var rawJson = usage.RawJson;
        var accountName = usage.AccountName;
        var configKey = usage.ConfigKey;
        var upstreamResponseValidity = usage.UpstreamResponseValidity;
        var upstreamResponseNote = usage.UpstreamResponseNote;
        if (isPrivacyMode)
        {
            if (!string.IsNullOrWhiteSpace(rawJson) ||
                !string.IsNullOrWhiteSpace(accountName) ||
                !string.IsNullOrWhiteSpace(configKey))
            {
                privacyRedactedCount++;
            }

            rawJson = null;
            accountName = string.Empty;
            configKey = string.Empty;
        }

        var normalizedUsageCandidate = new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = providerName,
            ParentProviderId = usage.ParentProviderId,
            RequestsUsed = requestsUsed,
            RequestsAvailable = requestsAvailable,
            UsedPercent = requestsPercentage,
            PlanType = usage.PlanType,
            IsQuotaBased = usage.IsQuotaBased,
            DisplayAsFraction = usage.DisplayAsFraction,
            IsAvailable = usage.IsAvailable,
            State = usage.State,
            IsStatusOnly = usage.IsStatusOnly,
            IsCurrencyUsage = usage.IsCurrencyUsage,
            Description = description,
            AuthSource = usage.AuthSource,
            Details = details,
            AccountName = accountName ?? string.Empty,
            ConfigKey = configKey ?? string.Empty,
            NextResetTime = normalizedNextResetTimeUtc,
            FetchedAt = fetchedAt,
            ResponseLatencyMs = responseLatencyMs,
            RawJson = rawJson,
            HttpStatus = httpStatus,
            UpstreamResponseValidity = upstreamResponseValidity,
            UpstreamResponseNote = upstreamResponseNote ?? string.Empty,
        };
        var upstreamEvaluation = UpstreamResponseValidityCatalog.Evaluate(normalizedUsageCandidate);
        upstreamResponseValidity = upstreamEvaluation.Validity;
        upstreamResponseNote = upstreamEvaluation.Note;

        if (!this.StringEquals(providerId, usage.ProviderId) ||
            !this.StringEquals(providerName, usage.ProviderName) ||
            requestsUsed != usage.RequestsUsed ||
            requestsAvailable != usage.RequestsAvailable ||
            requestsPercentage != usage.UsedPercent ||
            responseLatencyMs != usage.ResponseLatencyMs ||
            fetchedAt != usage.FetchedAt ||
            !this.StringEquals(description, usage.Description) ||
            httpStatus != usage.HttpStatus ||
            normalizedNextResetTimeUtc != usageNextResetTimeUtc ||
            !ReferenceEquals(details, usage.Details) ||
            upstreamResponseValidity != usage.UpstreamResponseValidity ||
            !this.StringEquals(upstreamResponseNote, usage.UpstreamResponseNote))
        {
            normalizedCount++;
        }

        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = providerName,
            ParentProviderId = usage.ParentProviderId,
            RequestsUsed = requestsUsed,
            RequestsAvailable = requestsAvailable,
            UsedPercent = requestsPercentage,
            PlanType = usage.PlanType,
            IsQuotaBased = usage.IsQuotaBased,
            DisplayAsFraction = usage.DisplayAsFraction,
            IsAvailable = usage.IsAvailable,
            State = usage.State,
            IsStatusOnly = usage.IsStatusOnly,
            IsCurrencyUsage = usage.IsCurrencyUsage,
            Description = description,
            AuthSource = usage.AuthSource,
            Details = details,
            AccountName = accountName ?? string.Empty,
            ConfigKey = configKey ?? string.Empty,
            NextResetTime = normalizedNextResetTimeUtc,
            FetchedAt = fetchedAt,
            ResponseLatencyMs = responseLatencyMs,
            RawJson = rawJson,
            HttpStatus = httpStatus,
            UpstreamResponseValidity = upstreamResponseValidity,
            UpstreamResponseNote = upstreamResponseNote ?? string.Empty,
        };
    }

    private bool StringEquals(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    private DateTime NormalizeFetchedAt(DateTime fetchedAt)
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

    private double SanitizeNonNegativeFinite(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
        {
            return 0;
        }

        return value;
    }

    private double NormalizePercentage(ProviderUsage usage, double requestsUsed, double requestsAvailable)
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

    private IReadOnlyList<ProviderUsageDetail>? NormalizeDetails(IReadOnlyList<ProviderUsageDetail>? details)
    {
        if (details == null || details.Count == 0)
        {
            return null;
        }

        var normalizedDetails = new List<ProviderUsageDetail>(details.Count);
        foreach (var detail in details)
        {
            normalizedDetails.Add(new ProviderUsageDetail
            {
                Name = (detail.Name ?? string.Empty).Trim(),
                ModelName = (detail.ModelName ?? string.Empty).Trim(),
                GroupName = (detail.GroupName ?? string.Empty).Trim(),
                Description = (detail.Description ?? string.Empty).Trim(),
                NextResetTime = detail.NextResetTime?.ToUniversalTime(),
                DetailType = detail.DetailType,
                QuotaBucketKind = detail.QuotaBucketKind,
                PercentageValue = detail.PercentageValue,
                PercentageSemantic = detail.PercentageSemantic,
                PercentageDecimalPlaces = detail.PercentageDecimalPlaces,
                IsStale = detail.IsStale,
            });
        }

        return normalizedDetails;
    }

    private bool IsUsageForAnyActiveProvider(HashSet<string> activeProviderIds, string usageProviderId)
    {
        return activeProviderIds.Any(providerId => this.IsUsageForProvider(providerId, usageProviderId));
    }

    private bool IsUsageForProvider(string providerId, string usageProviderId)
    {
        return ProviderMetadataCatalog.BelongsToProviderFamily(providerId, usageProviderId);
    }

    private bool IsPlaceholderUnavailableUsage(ProviderUsage usage)
    {
        if (usage.RequestsAvailable != 0 ||
            usage.RequestsUsed != 0 ||
            usage.IsAvailable)
        {
            return false;
        }

        if (usage.State == ProviderUsageState.Missing || usage.State == ProviderUsageState.Unavailable)
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(usage.Description);
    }

    private bool TryCreateDetailContractErrorUsage(
        ProviderUsage usage,
        out ProviderUsage invalidUsage)
    {
        invalidUsage = usage;
        if (usage.Details == null)
        {
            return false;
        }

        var validationErrors = new List<string>();
        foreach (var detail in usage.Details)
        {
            if (string.IsNullOrWhiteSpace(detail.Name))
            {
                validationErrors.Add("Detail Name is empty");
            }

            if (detail.DetailType == ProviderUsageDetailType.Unknown)
            {
                validationErrors.Add("DetailType is Unknown (must be QuotaWindow, Credit, Model, or Other)");
            }

            if (detail.DetailType == ProviderUsageDetailType.QuotaWindow &&
                detail.QuotaBucketKind == WindowKind.None)
            {
                validationErrors.Add("QuotaWindow details must have WindowKind set (Burst, Rolling, or ModelSpecific)");
            }
        }

        if (validationErrors.Count == 0)
        {
            return false;
        }

        invalidUsage = new ProviderUsage
        {
            ProviderId = usage.ProviderId,
            ProviderName = usage.ProviderName,
            RequestsUsed = 0,
            RequestsAvailable = 0,
            UsedPercent = 0,
            PlanType = usage.PlanType,
            IsQuotaBased = usage.IsQuotaBased,
            DisplayAsFraction = usage.DisplayAsFraction,
            IsAvailable = false,
            State = ProviderUsageState.Error,
            Description = $"Invalid detail contract: {string.Join("; ", validationErrors)}",
            AuthSource = usage.AuthSource,
            Details = null,
            AccountName = usage.AccountName,
            ConfigKey = usage.ConfigKey,
            NextResetTime = null,
            FetchedAt = usage.FetchedAt,
            ResponseLatencyMs = usage.ResponseLatencyMs,
            RawJson = usage.RawJson,
            HttpStatus = usage.HttpStatus,
            UpstreamResponseValidity = UpstreamResponseValidity.Invalid,
            UpstreamResponseNote = "Invalid detail contract",
        };

        return true;
    }

    private void NormalizeFamilyAccountIdentity(List<ProviderUsage> usages)
    {
        foreach (var group in usages.GroupBy(
                     usage => ProviderMetadataCatalog.GetCanonicalProviderId(usage.ProviderId),
                     StringComparer.OrdinalIgnoreCase))
        {
            var resolvedAccountName = group
                .Select(usage => usage.AccountName?.Trim())
                .FirstOrDefault(accountName => !string.IsNullOrWhiteSpace(accountName));

            if (string.IsNullOrWhiteSpace(resolvedAccountName))
            {
                continue;
            }

            foreach (var usage in group)
            {
                if (string.IsNullOrWhiteSpace(usage.AccountName))
                {
                    usage.AccountName = resolvedAccountName;
                }
            }
        }
    }
}
