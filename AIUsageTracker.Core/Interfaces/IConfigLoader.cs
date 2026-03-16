// <copyright file="IConfigLoader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IConfigLoader
{
    Task<IReadOnlyList<ProviderConfig>> LoadConfigAsync();

    Task SaveConfigAsync(IEnumerable<ProviderConfig> configs);

    Task<AppPreferences> LoadPreferencesAsync();

    Task SavePreferencesAsync(AppPreferences preferences);
}
