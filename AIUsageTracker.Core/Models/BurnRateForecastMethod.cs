// <copyright file="BurnRateForecastMethod.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public enum BurnRateForecastMethod
{
    SimpleAverage,
    LinearRegression,
    MovingAverage,
    ExponentialSmoothing,
}
