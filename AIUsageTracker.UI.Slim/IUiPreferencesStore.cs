// <copyright file="IUiPreferencesStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.UI.Slim;

/// <summary>
/// Desktop-specific alias for <see cref="IPreferencesStore"/>.
/// Kept for backward compatibility with existing DI registrations.
/// </summary>
public interface IUiPreferencesStore : IPreferencesStore
{
}
