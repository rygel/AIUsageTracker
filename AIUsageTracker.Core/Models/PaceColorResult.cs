// <copyright file="PaceColorResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Single result of all pace/color/projection computations for one usage value.
/// Produced by <see cref="UsageMath.ComputePaceColor"/> — the only method
/// callers should use for pace-related decisions.
/// </summary>
public readonly record struct PaceColorResult(
    /// <summary>Raw used percent (0–100). For non-pace-adjusted cards: compare against yellow/red thresholds. For pace-adjusted cards: tier drives color, this value is ignored.</summary>
    double ColorPercent,

    /// <summary>Three-tier pace classification.</summary>
    PaceTier PaceTier,

    /// <summary>Projected end-of-period usage percentage (unclamped, can exceed 100).</summary>
    double ProjectedPercent,

    /// <summary>Badge display text ("Headroom", "On pace", "Over pace"), empty when not pace-adjusted.</summary>
    string BadgeText,

    /// <summary>Projected delta text ("+12%", "-30%", "73%"), empty when not pace-adjusted.</summary>
    string ProjectedText,

    /// <summary>Whether pace adjustment was actually applied (false when data was missing or disabled).</summary>
    bool IsPaceAdjusted);
