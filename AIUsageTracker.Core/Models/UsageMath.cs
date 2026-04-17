// <copyright file="UsageMath.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.RegularExpressions;

namespace AIUsageTracker.Core.Models;

public static class UsageMath
{
    private const double MinimumDropRatioForReset = 0.2;
    private const double MinimumElapsedHours = 1.0;
    private const double AnomalySigmaThreshold = 3.0;
    private const double AnomalySigmaEpsilon = 0.001;
    private const double AnomalyMadScale = 1.4826;
    private const double MinimumAbsoluteRateDeltaPerDay = 1.0;

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private static readonly Regex SUsedPattern = new(
        @"(?<percent>\d+(?:\.\d+)?)\s*%\s*used",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex SRemainingPattern = new(
        @"(?<percent>\d+(?:\.\d+)?)\s*%\s*remaining",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex SPercentPattern = new(
        @"(?<percent>\d+(?:\.\d+)?)\s*%",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    private static readonly Regex SNumericPattern = new(
        @"^\s*(?<percent>\d+(?:\.\d+)?)\s*$",
        RegexOptions.CultureInvariant,
        RegexTimeout);

    /// <summary>
    /// Clamps a percentage value to the range [0, 100], handling NaN and Infinity.
    /// </summary>
    /// <returns></returns>
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
    /// <param name="used">Number of items used.</param>
    /// <param name="total">Total number of items.</param>
    /// <returns>Percentage used (0-100), or 0 if total is invalid.</returns>
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
    /// <param name="used">Number of items used.</param>
    /// <param name="total">Total number of items.</param>
    /// <returns>Percentage remaining (0-100), or 100 if total is invalid.</returns>
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
    /// <returns></returns>
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
    /// <param name="value">The value to calculate percentage for.</param>
    /// <param name="of">The total/reference value.</param>
    /// <returns>Percentage (0-100), or 0 if reference is invalid.</returns>
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
    /// <param name="value">The string to parse.</param>
    /// <param name="isUsed">Output parameter indicating if the value was explicitly marked as 'used'.</param>
    /// <returns>The parsed percentage (0-100), or null if parsing failed.</returns>
    public static double? ParsePercent(string? value, out bool? isUsed)
    {
        isUsed = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TryParseUsedPercent(value, out var usedPercent))
        {
            isUsed = true;
            return ClampPercent(usedPercent);
        }

        if (TryParseRemainingPercent(value, out var remainingPercent))
        {
            isUsed = false;
            return ClampPercent(remainingPercent);
        }

        if (TryParseGenericPercent(value, out var percent, out var genericIsUsed))
        {
            isUsed = genericIsUsed;
            return ClampPercent(percent);
        }

        if (TryParseNumericPercent(value, out var numericPercent))
        {
            return ClampPercent(numericPercent);
        }

        return null;
    }

    /// <summary>
    /// Simple wrapper for ParsePercent when 'isUsed' info is not needed.
    /// </summary>
    /// <returns></returns>
    public static double? ParsePercent(string? value) => ParsePercent(value, out _);

    public static double GetEffectiveUsedPercent(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        return ClampPercent(usage.UsedPercent);
    }

    /// <summary>
    /// Infers the next reset time from a list of flat provider usage cards.
    /// Prefers the nearest future <see cref="ProviderUsage.NextResetTime"/> value;
    /// falls back to the most recent known reset time if no future reset is found.
    /// </summary>
    /// <param name="cards">The list of provider usage cards to inspect.</param>
    /// <returns>The inferred reset time, or null if no reset time is available.</returns>
    public static DateTime? InferResetTimeFromCards(IReadOnlyList<ProviderUsage>? cards)
    {
        if (cards == null || cards.Count == 0)
        {
            return null;
        }

        var nowUtc = DateTime.UtcNow;
        var resetTimes = cards
            .Where(c => c.NextResetTime.HasValue)
            .Select(c => c.NextResetTime!.Value.ToUniversalTime())
            .ToList();

        if (resetTimes.Count == 0)
        {
            return null;
        }

        var lastKnown = resetTimes.Max();
        var bestFuture = resetTimes.Where(t => t > nowUtc).Cast<DateTime?>().Min();

        return bestFuture ?? lastKnown;
    }

    /// <summary>
    /// Returns the pace badge result for an already-computed projected percent.
    /// </summary>
    /// <returns></returns>
    public static PaceBadgeResult ClassifyPace(double projectedPercent)
    {
        var tier = projectedPercent switch
        {
            >= 100.0 => PaceTier.OverPace,
            >= 70.0 => PaceTier.OnPace,
            _ => PaceTier.Headroom,
        };

        return new PaceBadgeResult(tier, projectedPercent);
    }

    /// <summary>
    /// Single computation of pace-adjusted color, projected percent, and pace tier.
    /// All UI and alert code should call this instead of the individual methods.
    /// The result guarantees that badge tier and color percent always agree.
    /// </summary>
    /// <returns></returns>
    public static PaceColorResult ComputePaceColor(
        double usedPercent,
        DateTime? nextResetTime,
        TimeSpan? periodDuration,
        bool enablePaceAdjustment = true,
        DateTime? nowUtc = null)
    {
        if (!enablePaceAdjustment || !periodDuration.HasValue || !nextResetTime.HasValue)
        {
            return new PaceColorResult(
                ColorPercent: ClampPercent(usedPercent),
                PaceTier: PaceTier.OnPace,
                ProjectedPercent: ClampPercent(usedPercent),
                BadgeText: string.Empty,
                ProjectedText: string.Empty,
                IsPaceAdjusted: false);
        }

        var nextReset = nextResetTime.Value.ToUniversalTime();
        var period = periodDuration.Value;

        if (period.TotalSeconds <= 0 || nextReset.Ticks < period.Ticks)
        {
            return new PaceColorResult(
                ColorPercent: ClampPercent(usedPercent),
                PaceTier: PaceTier.OnPace,
                ProjectedPercent: ClampPercent(usedPercent),
                BadgeText: string.Empty,
                ProjectedText: string.Empty,
                IsPaceAdjusted: false);
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var periodStart = nextReset - period;
        var elapsed = now - periodStart;
        var elapsedFraction = Math.Clamp(elapsed.TotalSeconds / period.TotalSeconds, 0.01, 1.0);

        var projected = usedPercent / elapsedFraction;

        var tier = projected switch
        {
            >= 100.0 => PaceTier.OverPace,
            >= 70.0 => PaceTier.OnPace,
            _ => PaceTier.Headroom,
        };

        var badge = new PaceBadgeResult(tier, projected);

        return new PaceColorResult(
            ColorPercent: ClampPercent(usedPercent),
            PaceTier: tier,
            ProjectedPercent: ClampPercent(projected),
            BadgeText: badge.Text,
            ProjectedText: badge.ProjectedText,
            IsPaceAdjusted: true);
    }

    /// <summary>
    /// Calculates how many days have elapsed in the current period. Returns 0 if data is invalid.
    /// </summary>
    /// <returns></returns>
    public static double GetElapsedDays(DateTime? nextResetTime, TimeSpan? periodDuration, DateTime? nowUtc = null)
    {
        if (!nextResetTime.HasValue || !periodDuration.HasValue || periodDuration.Value.TotalDays < 1)
        {
            return 0;
        }

        var nextReset = nextResetTime.Value.ToUniversalTime();
        if (nextReset.Ticks < periodDuration.Value.Ticks)
        {
            return 0;
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var periodStart = nextReset - periodDuration.Value;
        return Math.Max(0, (now - periodStart).TotalDays);
    }

    public static string FormatAbsoluteTime(DateTime nextReset)
    {
        var utc = AsUtc(nextReset);
        var local = utc.ToLocalTime();
        var diff = utc - DateTime.UtcNow;
        if (diff.TotalSeconds <= 0)
        {
            return "now";
        }

        if (local.Date == DateTime.Today)
        {
            return local.ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        if (local.Date == DateTime.Today.AddDays(1))
        {
            return $"Tomorrow {local.ToString("HH:mm", CultureInfo.InvariantCulture)}";
        }

        return diff.TotalDays < 7 ? $"{local.ToString("dddd HH:mm", CultureInfo.InvariantCulture)}" : $"{local.ToString("MMM d HH:mm", CultureInfo.InvariantCulture)}";
    }

    public static string FormatAbsoluteDate(DateTime nextReset)
    {
        var local = AsUtc(nextReset).ToLocalTime();
        return $"{local.ToString("MMM d, HH:mm", CultureInfo.InvariantCulture)}";
    }

    public static string FormatRelativeTime(DateTime nextReset)
    {
        var utc = AsUtc(nextReset);
        var diff = utc - DateTime.UtcNow;
        if (diff.TotalSeconds <= 0)
        {
            return "0m";
        }

        if (diff.TotalDays >= 1)
        {
            return $"{diff.Days.ToString(CultureInfo.InvariantCulture)}d {diff.Hours.ToString(CultureInfo.InvariantCulture)}h";
        }

        return diff.TotalHours >= 1 ? $"{diff.Hours.ToString(CultureInfo.InvariantCulture)}h {diff.Minutes.ToString(CultureInfo.InvariantCulture)}m" : $"{diff.Minutes.ToString(CultureInfo.InvariantCulture)}m";
    }

    /// <summary>
    /// Ensures a DateTime is treated as UTC. Unspecified kinds (e.g. from database
    /// round-trip via Dapper/SQLite) are assumed UTC. Local kinds are converted.
    /// All internal code should use UTC; convert to local only at the display boundary.
    /// </summary>
    /// <returns></returns>
    public static DateTime AsUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value.ToUniversalTime(),
        };
    }

    public static BurnRateForecast CalculateBurnRateForecast(IEnumerable<ProviderUsage> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var samples = FilterValidSamples(history).ToList();
        if (!ValidateMinimumSamples(samples, 2, "Insufficient history", out var forecastResult))
        {
            return forecastResult;
        }

        // Check for no consumption trend - all samples have same usage (before any trimming or validation)
        var firstUsage = samples[0].RequestsUsed;
        var allSame = samples.TrueForAll(x => Math.Abs(x.RequestsUsed - firstUsage) < 0.001);
        if (allSame)
        {
            return BurnRateForecast.Unavailable("No consumption trend");
        }

        var cycleSamples = TrimToLatestCycle(samples);
        if (!ValidateMinimumSamples(cycleSamples, 2, "Insufficient cycle history", out forecastResult))
        {
            return forecastResult;
        }

        if (!ValidateTimeWindow(cycleSamples, out forecastResult))
        {
            return forecastResult;
        }

        var burnRatePerDay = CalculateBurnRatePerDay(cycleSamples, out _);
        if (!ValidateBurnRate(burnRatePerDay, out forecastResult))
        {
            return forecastResult;
        }

        return CreateBurnRateForecast(burnRatePerDay, cycleSamples);
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
            LastSeenUtc = samples[^1].FetchedAt.ToUniversalTime(),
        };
    }

    public static UsageAnomalySnapshot CalculateUsageAnomalySnapshot(IEnumerable<ProviderUsage> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var samples = FilterValidSamplesForAnomaly(history);
        if (!ValidateMinimumSamplesForAnomaly(samples, 4, "Insufficient history", out var snapshotResult))
        {
            return snapshotResult;
        }

        var cycleSamples = TrimToLatestCycle(samples);
        if (!ValidateMinimumSamplesForAnomaly(cycleSamples, 4, "Insufficient cycle history", out snapshotResult))
        {
            return snapshotResult;
        }

        var rates = CalculateRatesPerDay(cycleSamples);
        if (!ValidateRatesAndCalculateBaseline(rates, out var baselineMedian, out var baselineRates, out var latest, out var snapshot))
        {
            return snapshot;
        }

        return CreateAnomalySnapshot(baselineMedian, baselineRates, latest, cycleSamples);
    }

    private static bool TryParseUsedPercent(string value, out double percent)
    {
        percent = 0;
        var usedMatch = SUsedPattern.Match(value);
        if (!usedMatch.Success)
        {
            return false;
        }

        return double.TryParse(
            usedMatch.Groups["percent"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out percent);
    }

    private static bool TryParseRemainingPercent(string value, out double percent)
    {
        percent = 0;
        var remainingMatch = SRemainingPattern.Match(value);
        if (!remainingMatch.Success)
        {
            return false;
        }

        return double.TryParse(
            remainingMatch.Groups["percent"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out percent);
    }

    private static bool TryParseGenericPercent(string value, out double percent, out bool? isUsed)
    {
        percent = 0;
        isUsed = null;

        var match = SPercentPattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        if (value.Contains("used", StringComparison.OrdinalIgnoreCase))
        {
            isUsed = true;
        }
        else if (value.Contains("remaining", StringComparison.OrdinalIgnoreCase))
        {
            isUsed = false;
        }

        return double.TryParse(
            match.Groups["percent"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out percent);
    }

    private static bool TryParseNumericPercent(string value, out double percent)
    {
        percent = 0;
        var match = SNumericPattern.Match(value);
        if (!match.Success)
        {
            return false;
        }

        return double.TryParse(
            match.Groups["percent"].Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out percent);
    }

    private static List<ProviderUsage> FilterValidSamples(IEnumerable<ProviderUsage> history)
    {
        return history
            .Where(x => x.FetchedAt != default && x.RequestsAvailable > 0 && !double.IsNaN(x.RequestsUsed))
            .OrderBy(x => x.FetchedAt)
            .ToList();
    }

    private static bool ValidateMinimumSamples(List<ProviderUsage> samples, int minimum, string errorMessage, out BurnRateForecast forecast)
    {
        forecast = BurnRateForecast.Unavailable(errorMessage);
        return samples.Count >= minimum;
    }

    private static bool ValidateTimeWindow(List<ProviderUsage> cycleSamples, out BurnRateForecast forecast)
    {
        forecast = BurnRateForecast.Unavailable("Insufficient time window");
        var first = cycleSamples[0];
        var last = cycleSamples[^1];
        var elapsedDays = (last.FetchedAt - first.FetchedAt).TotalDays;
        return elapsedDays > 0 && (last.FetchedAt - first.FetchedAt).TotalHours >= MinimumElapsedHours;
    }

    private static double CalculateBurnRatePerDay(List<ProviderUsage> cycleSamples, out double elapsedDays)
    {
        var first = cycleSamples[0];
        var last = cycleSamples[^1];
        elapsedDays = (last.FetchedAt - first.FetchedAt).TotalDays;

        double positiveIncrease = 0;
        for (var i = 1; i < cycleSamples.Count; i++)
        {
            var delta = cycleSamples[i].RequestsUsed - cycleSamples[i - 1].RequestsUsed;
            if (delta > 0)
            {
                positiveIncrease += delta;
            }
        }

        return positiveIncrease / elapsedDays;
    }

    private static bool ValidateBurnRate(double burnRatePerDay, out BurnRateForecast forecast)
    {
        forecast = BurnRateForecast.Unavailable("Invalid burn rate");
        return burnRatePerDay > 0 && !double.IsNaN(burnRatePerDay) && !double.IsInfinity(burnRatePerDay);
    }

    private static BurnRateForecast CreateBurnRateForecast(double burnRatePerDay, List<ProviderUsage> cycleSamples)
    {
        var last = cycleSamples[^1];
        var remaining = Math.Max(0, last.RequestsAvailable - last.RequestsUsed);
        var daysRemaining = remaining <= 0 ? 0 : remaining / burnRatePerDay;

        if (double.IsNaN(daysRemaining) || double.IsInfinity(daysRemaining))
        {
            return BurnRateForecast.Unavailable("Invalid forecast");
        }

        // Check for no consumption trend - all samples have same usage
        var hasNoTrend = cycleSamples.TrueForAll(x => Math.Abs(x.RequestsUsed - cycleSamples[0].RequestsUsed) < 0.001);
        if (hasNoTrend)
        {
            return BurnRateForecast.Unavailable("No consumption trend");
        }

        return new BurnRateForecast
        {
            IsAvailable = true,
            BurnRatePerDay = burnRatePerDay,
            RemainingUnits = remaining,
            DaysUntilExhausted = daysRemaining,
            EstimatedExhaustionUtc = last.FetchedAt.ToUniversalTime().AddDays(daysRemaining),
            SampleCount = cycleSamples.Count,
            TrendDirection = TrendDirection.Stable,
        };
    }

    private static List<ProviderUsage> FilterValidSamplesForAnomaly(IEnumerable<ProviderUsage> history)
    {
        return history
            .Where(x => x.FetchedAt != default && x.RequestsAvailable > 0 && !double.IsNaN(x.RequestsUsed))
            .OrderBy(x => x.FetchedAt)
            .ToList();
    }

    private static bool ValidateMinimumSamplesForAnomaly(List<ProviderUsage> samples, int minimum, string errorMessage, out UsageAnomalySnapshot snapshot)
    {
        snapshot = UsageAnomalySnapshot.Unavailable(errorMessage);
        return samples.Count >= minimum;
    }

    private static List<(double RatePerDay, DateTime FetchedAt)> CalculateRatesPerDay(List<ProviderUsage> cycleSamples)
    {
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

        return rates;
    }

    private static bool ValidateRatesAndCalculateBaseline(List<(double RatePerDay, DateTime FetchedAt)> rates, out double baselineMedian, out List<double> baselineRates, out (double RatePerDay, DateTime FetchedAt) latest, out UsageAnomalySnapshot snapshot)
    {
        snapshot = UsageAnomalySnapshot.Unavailable("Insufficient trend data");
        baselineMedian = 0;
        baselineRates = new List<double>();
        latest = (0, DateTime.MinValue);

        if (rates.Count < 3)
        {
            return false;
        }

        snapshot = UsageAnomalySnapshot.Unavailable("Insufficient baseline");
        latest = rates[^1];
        baselineRates = rates.Take(rates.Count - 1).Select(x => x.RatePerDay).ToList();
        if (baselineRates.Count < 2)
        {
            return false;
        }

        baselineMedian = Median(baselineRates);
        return true;
    }

    private static UsageAnomalySnapshot CreateAnomalySnapshot(double baselineMedian, List<double> baselineRates, (double RatePerDay, DateTime FetchedAt) latest, List<ProviderUsage> cycleSamples)
    {
        var mad = Median(baselineRates.Select(x => Math.Abs(x - baselineMedian)).ToList());
        var sigma = mad * AnomalyMadScale;
        if (sigma < AnomalySigmaEpsilon)
        {
            sigma = StandardDeviation(baselineRates);
        }

        var delta = latest.RatePerDay - baselineMedian;
        var minimumDelta = Math.Max(MinimumAbsoluteRateDeltaPerDay, Math.Abs(baselineMedian) * 0.25);

        var (hasAnomaly, sigmaDistance) = DetermineAnomalyStatus(delta, sigma, minimumDelta);

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
            LastDetectedUtc = hasAnomaly ? latest.FetchedAt.ToUniversalTime() : null,
        };
    }

    private static (bool HasAnomaly, double SigmaDistance) DetermineAnomalyStatus(double delta, double sigma, double minimumDelta)
    {
        if (sigma < AnomalySigmaEpsilon)
        {
            var hasAnomaly = Math.Abs(delta) >= minimumDelta * 2;
            return (hasAnomaly, hasAnomaly ? double.PositiveInfinity : 0);
        }
        else
        {
            var sigmaDistance = Math.Abs(delta) / sigma;
            var hasAnomaly = sigmaDistance >= AnomalySigmaThreshold && Math.Abs(delta) >= minimumDelta;
            return (hasAnomaly, sigmaDistance);
        }
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
            _ => "Low",
        };
    }
}
