namespace AIUsageTracker.Core.Models;

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
