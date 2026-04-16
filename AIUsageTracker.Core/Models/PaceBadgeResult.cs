// <copyright file="PaceBadgeResult.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Runtime.InteropServices;

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Three-tier pace classification for quota windows.
/// </summary>
public enum PaceTier
{
    /// <summary>Projected usage under 70% — plenty of room.</summary>
    Headroom,

    /// <summary>Projected usage 70–100% — normal pace.</summary>
    OnPace,

    /// <summary>Projected usage over 100% — will exhaust before reset.</summary>
    OverPace,
}

/// <summary>
/// Result of a pace classification, including the tier, display text, and projected percentage.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct PaceBadgeResult(PaceTier Tier, double ProjectedPercent)
{
    /// <summary>
    /// Gets the display text for the pace badge.
    /// </summary>
    public string Text => this.Tier switch
    {
        PaceTier.Headroom => "Headroom",
        PaceTier.OnPace => "On pace",
        PaceTier.OverPace => "Over pace",
        _ => string.Empty,
    };

    /// <summary>
    /// Gets formatted projected usage text with delta, e.g. "Projected: 73% (+5%)".
    /// </summary>
    public string ProjectedText => this.Tier switch
    {
        PaceTier.OverPace => $"+{(this.ProjectedPercent - 100).ToString("F0", CultureInfo.InvariantCulture)}%",
        PaceTier.Headroom => $"-{(100 - this.ProjectedPercent).ToString("F0", CultureInfo.InvariantCulture)}%",
        _ => $"{this.ProjectedPercent.ToString("F0", CultureInfo.InvariantCulture)}%",
    };
}
