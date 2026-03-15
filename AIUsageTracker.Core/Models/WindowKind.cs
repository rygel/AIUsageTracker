// <copyright file="WindowKind.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Identifies the type of quota window for usage tracking.
/// </summary>
/// <remarks>
/// <para>Semantic meanings:</para>
/// <list type="bullet">
/// <item><description>Burst: Short-term burst limit, e.g., 3-hour or 5-hour quota</description></item>
/// <item><description>Rolling: Long-term rolling limit, e.g., 7-day or weekly quota</description></item>
/// <item><description>ModelSpecific: Model-specific limit, e.g., Codex Spark model quota</description></item>
/// </list>
/// </remarks>
public enum WindowKind
{
    /// <summary>
    /// No specific window type.
    /// </summary>
    None = 0,

    /// <summary>
    /// Short-term burst limit window (e.g., 3-hour or 5-hour quota).
    /// </summary>
    Burst = 1,

    /// <summary>
    /// Long-term rolling limit window (e.g., 7-day or weekly quota).
    /// </summary>
    Rolling = 2,

    /// <summary>
    /// Model-specific limit window (e.g., Codex Spark model quota).
    /// </summary>
    ModelSpecific = 3,
}
