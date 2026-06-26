// <copyright file="ThresholdTier.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Represents the severity tier for a usage percentage based on configurable thresholds.
/// Each rendering layer (WPF brushes, CSS classes) maps this enum to its own representation.
/// </summary>
public enum ThresholdTier
{
    Green,
    Yellow,
    Red,
}
