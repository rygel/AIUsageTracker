// <copyright file="BurnRateForecast.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models
{
    public sealed class BurnRateForecast
    {
        public bool IsAvailable { get; init; }

        public double BurnRatePerDay { get; init; }

        public double RemainingUnits { get; init; }

        public double DaysUntilExhausted { get; init; }

        public DateTime? EstimatedExhaustionUtc { get; init; }

        public int SampleCount { get; init; }

        public string? Reason { get; init; }

        public BurnRateForecastMethod Method { get; init; }

        public ConfidenceLevel Confidence { get; init; }

        public double? ConfidenceLowerBound { get; init; }

        public double? ConfidenceUpperBound { get; init; }

        public TrendDirection TrendDirection { get; init; }

        public TrendDirection Trend { get; init; }

        public static BurnRateForecast Unavailable(string reason)
        {
            return new BurnRateForecast
            {
                IsAvailable = false,
                Reason = reason,
            };
        }
    }

    public enum BurnRateForecastMethod
    {
        SimpleAverage,
        LinearRegression,
        MovingAverage,
        ExponentialSmoothing,
    }

    public enum ConfidenceLevel
    {
        Low,
        Medium,
        High,
    }

    public enum TrendDirection
    {
        Increasing,
        Decreasing,
        Stable,
    }
}
