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
/// <param name="DetailName">Expected <c>ProviderUsageDetail.Name</c> value used to match this declaration to a runtime detail row. When null, matching uses <see cref="Kind"/> only (sufficient when each Kind appears at most once).</param>
public sealed record QuotaWindowDefinition(
    WindowKind Kind,
    string DualBarLabel,
    string? ChildProviderId = null,
    string? SettingsLabel = null,
    string? DetailName = null);
