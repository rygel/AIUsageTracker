namespace AIUsageTracker.Core.Models;

public static class UsageMath
{
    private const double MinimumDropRatioForReset = 0.2;
    private const double MinimumElapsedHours = 1.0;
    private const double AnomalySigmaThreshold = 3.0;
    private const double AnomalySigmaEpsilon = 0.001;
    private const double AnomalyMadScale = 1.4826;
    private const double MinimumAbsoluteRateDeltaPerDay = 1.0;

    public static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0d, 100d);
    }

    public static double CalculateUsedPercent(double used, double total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return ClampPercent((used / total) * 100d);
    }

    public static double CalculateRemainingPercent(double used, double total)
    {
        if (total <= 0)
        {
            return 100;
        }

        return ClampPercent(((total - used) / total) * 100d);
    }

    public static double GetEffectiveUsedPercent(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var percentage = ClampPercent(usage.RequestsPercentage);
        var isQuota = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
        return isQuota ? ClampPercent(100 - percentage) : percentage;
    }

    public static BurnRateForecast CalculateBurnRateForecast(IEnumerable<ProviderUsage> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var samples = history
            .Where(x => x.FetchedAt != default && x.RequestsAvailable > 0 && !double.IsNaN(x.RequestsUsed))
            .OrderBy(x => x.FetchedAt)
            .ToList();

        if (samples.Count < 2)
        {
            return BurnRateForecast.Unavailable("Insufficient history");
        }

        var cycleSamples = TrimToLatestCycle(samples);
        if (cycleSamples.Count < 2)
        {
            return BurnRateForecast.Unavailable("Insufficient cycle history");
        }

        var first = cycleSamples[0];
        var last = cycleSamples[^1];
        var elapsedDays = (last.FetchedAt - first.FetchedAt).TotalDays;
        if (elapsedDays <= 0 || (last.FetchedAt - first.FetchedAt).TotalHours < MinimumElapsedHours)
        {
            return BurnRateForecast.Unavailable("Insufficient time window");
        }

        double positiveIncrease = 0;
        for (var i = 1; i < cycleSamples.Count; i++)
        {
            var delta = cycleSamples[i].RequestsUsed - cycleSamples[i - 1].RequestsUsed;
            if (delta > 0)
            {
                positiveIncrease += delta;
            }
        }

        if (positiveIncrease <= 0)
        {
            return BurnRateForecast.Unavailable("No consumption trend");
        }

        var burnRatePerDay = positiveIncrease / elapsedDays;
        if (burnRatePerDay <= 0 || double.IsNaN(burnRatePerDay) || double.IsInfinity(burnRatePerDay))
        {
            return BurnRateForecast.Unavailable("Invalid burn rate");
        }

        var remaining = Math.Max(0, last.RequestsAvailable - last.RequestsUsed);
        var daysRemaining = remaining <= 0 ? 0 : remaining / burnRatePerDay;
        if (double.IsNaN(daysRemaining) || double.IsInfinity(daysRemaining))
        {
            return BurnRateForecast.Unavailable("Invalid forecast");
        }

        return new BurnRateForecast
        {
            IsAvailable = true,
            BurnRatePerDay = burnRatePerDay,
            RemainingUnits = remaining,
            DaysUntilExhausted = daysRemaining,
            EstimatedExhaustionUtc = last.FetchedAt.ToUniversalTime().AddDays(daysRemaining),
            SampleCount = cycleSamples.Count
        };
    }

    public static ProviderReliabilitySnapshot CalculateReliabilitySnapshot(IEnumerable<ProviderUsage> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var samples = history
            .Where(x => x.FetchedAt != default)
            .OrderBy(x => x.FetchedAt)
            .ToList();

        if (samples.Count == 0)
        {
            return ProviderReliabilitySnapshot.Unavailable("No history");
        }

        var successCount = samples.Count(x => x.IsAvailable);
        var failureCount = samples.Count - successCount;
        var failureRatePercent = (failureCount / (double)samples.Count) * 100.0;

        var latencySamples = samples
            .Where(x => x.ResponseLatencyMs > 0)
            .Select(x => x.ResponseLatencyMs)
            .ToList();
        var averageLatencyMs = latencySamples.Count == 0 ? 0 : latencySamples.Average();
        var lastLatencyMs = latencySamples.Count == 0 ? 0 : latencySamples[^1];

        return new ProviderReliabilitySnapshot
        {
            IsAvailable = true,
            SampleCount = samples.Count,
            SuccessCount = successCount,
            FailureCount = failureCount,
            FailureRatePercent = failureRatePercent,
            AverageLatencyMs = averageLatencyMs,
            LastLatencyMs = lastLatencyMs,
            LastSuccessfulSyncUtc = samples.LastOrDefault(x => x.IsAvailable)?.FetchedAt.ToUniversalTime(),
            LastSeenUtc = samples[^1].FetchedAt.ToUniversalTime()
        };
    }

    public static UsageAnomalySnapshot CalculateUsageAnomalySnapshot(IEnumerable<ProviderUsage> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var samples = history
            .Where(x => x.FetchedAt != default && x.RequestsAvailable > 0 && !double.IsNaN(x.RequestsUsed))
            .OrderBy(x => x.FetchedAt)
            .ToList();

        if (samples.Count < 4)
        {
            return UsageAnomalySnapshot.Unavailable("Insufficient history");
        }

        var cycleSamples = TrimToLatestCycle(samples);
        if (cycleSamples.Count < 4)
        {
            return UsageAnomalySnapshot.Unavailable("Insufficient cycle history");
        }

        var rates = new List<(double RatePerDay, DateTime FetchedAt)>();
        for (var i = 1; i < cycleSamples.Count; i++)
        {
            var previous = cycleSamples[i - 1];
            var current = cycleSamples[i];
            var elapsedHours = (current.FetchedAt - previous.FetchedAt).TotalHours;
            if (elapsedHours <= 0)
            {
                continue;
            }

            var ratePerDay = ((current.RequestsUsed - previous.RequestsUsed) / elapsedHours) * 24.0;
            if (double.IsNaN(ratePerDay) || double.IsInfinity(ratePerDay))
            {
                continue;
            }

            rates.Add((ratePerDay, current.FetchedAt));
        }

        if (rates.Count < 3)
        {
            return UsageAnomalySnapshot.Unavailable("Insufficient trend data");
        }

        var latest = rates[^1];
        var baselineRates = rates
            .Take(rates.Count - 1)
            .Select(x => x.RatePerDay)
            .ToList();
        if (baselineRates.Count < 2)
        {
            return UsageAnomalySnapshot.Unavailable("Insufficient baseline");
        }

        var baselineMedian = Median(baselineRates);
        var mad = Median(baselineRates.Select(x => Math.Abs(x - baselineMedian)).ToList());
        var sigma = mad * AnomalyMadScale;
        if (sigma < AnomalySigmaEpsilon)
        {
            sigma = StandardDeviation(baselineRates);
        }

        var delta = latest.RatePerDay - baselineMedian;
        var minimumDelta = Math.Max(MinimumAbsoluteRateDeltaPerDay, Math.Abs(baselineMedian) * 0.25);

        double sigmaDistance;
        bool hasAnomaly;
        if (sigma < AnomalySigmaEpsilon)
        {
            hasAnomaly = Math.Abs(delta) >= minimumDelta * 2;
            sigmaDistance = hasAnomaly ? double.PositiveInfinity : 0;
        }
        else
        {
            sigmaDistance = Math.Abs(delta) / sigma;
            hasAnomaly = sigmaDistance >= AnomalySigmaThreshold && Math.Abs(delta) >= minimumDelta;
        }

        return new UsageAnomalySnapshot
        {
            IsAvailable = true,
            HasAnomaly = hasAnomaly,
            Direction = delta >= 0 ? "Spike" : "Drop",
            Severity = hasAnomaly ? GetAnomalySeverity(sigmaDistance) : "None",
            BaselineRatePerDay = baselineMedian,
            LatestRatePerDay = latest.RatePerDay,
            DeviationSigma = double.IsFinite(sigmaDistance) ? sigmaDistance : 999,
            SampleCount = cycleSamples.Count,
            LastDetectedUtc = hasAnomaly ? latest.FetchedAt.ToUniversalTime() : null
        };
    }

    private static List<ProviderUsage> TrimToLatestCycle(List<ProviderUsage> orderedSamples)
    {
        if (orderedSamples.Count < 2)
        {
            return orderedSamples;
        }

        var cycleStart = 0;
        for (var i = 1; i < orderedSamples.Count; i++)
        {
            var previous = orderedSamples[i - 1];
            var current = orderedSamples[i];
            var drop = previous.RequestsUsed - current.RequestsUsed;
            if (drop <= 0)
            {
                continue;
            }

            var reference = Math.Max(previous.RequestsUsed, previous.RequestsAvailable);
            var dropRatio = reference <= 0 ? 0 : drop / reference;
            if (dropRatio >= MinimumDropRatioForReset)
            {
                cycleStart = i;
            }
        }

        return orderedSamples.Skip(cycleStart).ToList();
    }

    private static double Median(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var ordered = values.OrderBy(x => x).ToList();
        var middle = ordered.Count / 2;
        if (ordered.Count % 2 == 0)
        {
            return (ordered[middle - 1] + ordered[middle]) / 2.0;
        }

        return ordered[middle];
    }

    private static double StandardDeviation(IReadOnlyCollection<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        var mean = values.Average();
        var variance = values.Sum(x => Math.Pow(x - mean, 2)) / values.Count;
        return Math.Sqrt(variance);
    }

    private static string GetAnomalySeverity(double sigmaDistance)
    {
        if (!double.IsFinite(sigmaDistance))
        {
            return "High";
        }

        return sigmaDistance switch
        {
            >= 6 => "High",
            >= 4 => "Medium",
            _ => "Low"
        };
    }
}

public sealed class BurnRateForecast
{
    public bool IsAvailable { get; init; }
    public double BurnRatePerDay { get; init; }
    public double RemainingUnits { get; init; }
    public double DaysUntilExhausted { get; init; }
    public DateTime? EstimatedExhaustionUtc { get; init; }
    public int SampleCount { get; init; }
    public string? Reason { get; init; }

    public static BurnRateForecast Unavailable(string reason)
    {
        return new BurnRateForecast
        {
            IsAvailable = false,
            Reason = reason
        };
    }
}

public sealed class ProviderReliabilitySnapshot
{
    public bool IsAvailable { get; init; }
    public int SampleCount { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public double FailureRatePercent { get; init; }
    public double AverageLatencyMs { get; init; }
    public double LastLatencyMs { get; init; }
    public DateTime? LastSuccessfulSyncUtc { get; init; }
    public DateTime? LastSeenUtc { get; init; }
    public string? Reason { get; init; }

    public static ProviderReliabilitySnapshot Unavailable(string reason)
    {
        return new ProviderReliabilitySnapshot
        {
            IsAvailable = false,
            Reason = reason
        };
    }
}

public sealed class UsageAnomalySnapshot
{
    public bool IsAvailable { get; init; }
    public bool HasAnomaly { get; init; }
    public string Direction { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public double BaselineRatePerDay { get; init; }
    public double LatestRatePerDay { get; init; }
    public double DeviationSigma { get; init; }
    public int SampleCount { get; init; }
    public DateTime? LastDetectedUtc { get; init; }
    public string? Reason { get; init; }

    public static UsageAnomalySnapshot Unavailable(string reason)
    {
        return new UsageAnomalySnapshot
        {
            IsAvailable = false,
            Reason = reason
        };
    }
}

