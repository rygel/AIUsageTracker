using System.Text.RegularExpressions;

namespace AIUsageTracker.Core.Models;

public static class UsageMath
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private const double MinimumDropRatioForReset = 0.2;
    private const double MinimumElapsedHours = 1.0;
    private const double AnomalySigmaThreshold = 3.0;
    private const double AnomalySigmaEpsilon = 0.001;
    private const double AnomalyMadScale = 1.4826;
    private const double MinimumAbsoluteRateDeltaPerDay = 1.0;

    private static readonly Regex s_usedPattern = new(
        @"(?<percent>\d+(?:\.\d+)?)\s*%\s*used",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex s_remainingPattern = new(
        @"(?<percent>\d+(?:\.\d+)?)\s*%\s*remaining",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex s_percentPattern = new(
        @"(?<percent>\d+(?:\.\d+)?)\s*%",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    /// <summary>
    /// Clamps a percentage value to the range [0, 100], handling NaN and Infinity.
    /// </summary>
    public static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0d, 100d);
    }

    /// <summary>
    /// Calculates the percentage of used items out of total.
    /// </summary>
    /// <param name="used">Number of items used</param>
    /// <param name="total">Total number of items</param>
    /// <returns>Percentage used (0-100), or 0 if total is invalid</returns>
    public static double CalculateUsedPercent(double used, double total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return ClampPercent((used / total) * 100d);
    }

    /// <summary>
    /// Calculates the percentage of remaining items out of total.
    /// </summary>
    /// <param name="used">Number of items used</param>
    /// <param name="total">Total number of items</param>
    /// <returns>Percentage remaining (0-100), or 100 if total is invalid</returns>
    public static double CalculateRemainingPercent(double used, double total)
    {
        if (total <= 0)
        {
            return 100;
        }

        return ClampPercent(((total - used) / total) * 100d);
    }

    /// <summary>
    /// Calculates the utilization percentage for display purposes.
    /// For quota-based providers, shows remaining percentage.
    /// For usage-based providers, shows used percentage.
    /// </summary>
    public static double CalculateUtilizationPercent(double used, double total, bool isQuotaBased)
    {
        if (total <= 0)
        {
            return isQuotaBased ? 100 : 0;
        }

        var usedPercent = CalculateUsedPercent(used, total);
        return isQuotaBased ? (100 - usedPercent) : usedPercent;
    }

    /// <summary>
    /// Calculates what percentage one value is of another.
    /// </summary>
    /// <param name="value">The value to calculate percentage for</param>
    /// <param name="of">The total/reference value</param>
    /// <returns>Percentage (0-100), or 0 if reference is invalid</returns>
    public static double PercentOf(double value, double of)
    {
        if (of <= 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return ClampPercent((value / of) * 100d);
    }

    /// <summary>
    /// Gets the effective used percentage for a provider, accounting for quota vs usage-based.
    /// </summary>
    /// <summary>
    /// Parses a percentage value from a string, handling optional '%' sign.
    /// If the string contains 'remaining', it is interpreted as a remaining percentage.
    /// If the string contains 'used', it is interpreted as a used percentage.
    /// </summary>
    /// <param name="value">The string to parse</param>
    /// <param name="isUsed">Output parameter indicating if the value was explicitly marked as 'used'</param>
    /// <returns>The parsed percentage (0-100), or null if parsing failed</returns>
    public static double? ParsePercent(string? value, out bool? isUsed)
    {
        isUsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var usedMatch = s_usedPattern.Match(value);
        if (usedMatch.Success)
        {
            isUsed = true;
            if (double.TryParse(
                    usedMatch.Groups["percent"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var usedPercent))
            {
                return ClampPercent(usedPercent);
            }
        }

        var remainingMatch = s_remainingPattern.Match(value);
        if (remainingMatch.Success)
        {
            isUsed = false;
            if (double.TryParse(
                    remainingMatch.Groups["percent"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var remainingPercent))
            {
                return ClampPercent(remainingPercent);
            }
        }

        var match = s_percentPattern.Match(value);
        if (match.Success)
        {
            if (value.Contains("used", StringComparison.OrdinalIgnoreCase))
            {
                isUsed = true;
            }
            else if (value.Contains("remaining", StringComparison.OrdinalIgnoreCase))
            {
                isUsed = false;
            }

            if (double.TryParse(
                    match.Groups["percent"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var percent))
            {
                return ClampPercent(percent);
            }
        }

        // Fallback for just numbers
        var cleanValue = new string(value.Where(c => char.IsDigit(c) || c == '.').ToArray());
        if (double.TryParse(cleanValue, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            return ClampPercent(result);
        }

        return null;
    }

    /// <summary>
    /// Simple wrapper for ParsePercent when 'isUsed' info is not needed.
    /// </summary>
    public static double? ParsePercent(string? value) => ParsePercent(value, out _);

    public static double GetEffectiveUsedPercent(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var percentage = ClampPercent(usage.RequestsPercentage);
        var isQuota = usage.IsQuotaBased;
        return isQuota ? ClampPercent(100 - percentage) : percentage;
    }

    /// <summary>
    /// Gets the effective used percentage for a provider detail, accounting for parent quota status
    /// and explicit 'used'/'remaining' strings.
    /// </summary>
    public static double? GetEffectiveUsedPercent(ProviderUsageDetail detail, bool parentIsQuota)
    {
        var val = ParsePercent(detail.Used, out var isUsed);
        if (!val.HasValue) return null;

        // 1. If explicitly marked as 'used', return as is.
        if (isUsed == true) return val.Value;

        // 2. If explicitly marked as 'remaining', return inverted.
        if (isUsed == false) return ClampPercent(100.0 - val.Value);

        // 3. Strict contract: Quota windows (DetailType=1) are ALWAYS remaining unless explicitly marked 'used'.
        // Antigravity sends "80%" which means 80% remaining.
        if (detail.DetailType == ProviderUsageDetailType.QuotaWindow || parentIsQuota)
        {
            return ClampPercent(100.0 - val.Value);
        }

        // 4. PAYG/Other details are used % by default
        return val.Value;
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

