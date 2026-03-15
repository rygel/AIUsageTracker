// <copyright file="ProviderUsageState.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Typed availability state for a provider usage snapshot.
/// Set by providers instead of embedding state signals in the Description string.
/// The Description field is display-only text once State is set.
/// </summary>
public enum ProviderUsageState
{
    /// <summary>Has live usage data. The normal operating state.</summary>
    Available = 0,

    /// <summary>Auth key or credential not found / provider not configured.</summary>
    Missing = 1,

    /// <summary>API or network error fetching usage. Description contains the error message.</summary>
    Error = 2,

    /// <summary>Output logged to console; user should check console for details.</summary>
    ConsoleCheck = 3,

    /// <summary>State is indeterminate or not yet fetched.</summary>
    Unknown = 4,

    /// <summary>Provider is configured but returned no usable data (e.g. empty response).</summary>
    Unavailable = 5,
}
