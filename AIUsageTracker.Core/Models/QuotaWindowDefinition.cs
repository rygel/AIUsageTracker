// <copyright file="QuotaWindowDefinition.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Declares a quota window for a provider, enabling declaration-based rendering
/// instead of runtime heuristics for label derivation, ID generation, and sort ordering.
/// </summary>
/// <param name="Kind">The quota bucket kind (Burst, Rolling, ModelSpecific).</param>
/// <param name="DualBarLabel">Short display label shown in the dual progress bar (e.g. "5h", "Weekly", "Sonnet").</param>
/// <param name="ChildProviderId">For SyntheticAggregateChildren providers: the explicit child provider ID (e.g. "claude-code.current-session").</param>
/// <param name="SettingsLabel">Human-readable label for the settings show/hide list. Falls back to <see cref="DualBarLabel"/> when null.</param>
/// <param name="DetailName">Reserved for legacy compatibility. No longer used for runtime matching.</param>
/// <param name="PeriodDuration">Duration of this quota window (e.g. <c>TimeSpan.FromHours(5)</c> for a burst window, <c>TimeSpan.FromDays(7)</c> for a weekly window). Used by the UI and alert service to compute time-adjusted pace so that progress-bar colours and threshold notifications are not false-positive when the user is under pace.</param>
public sealed record QuotaWindowDefinition(
    WindowKind Kind,
    string DualBarLabel,
    string? ChildProviderId = null,
    string? SettingsLabel = null,
    string? DetailName = null,
    TimeSpan? PeriodDuration = null);
