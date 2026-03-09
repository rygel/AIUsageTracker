// <copyright file="IConfigLoader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces
{
    using AIUsageTracker.Core.Models;

    public interface IConfigLoader
    {
        Task<IReadOnlyList<ProviderConfig>> LoadConfigAsync();

        Task SaveConfigAsync(IEnumerable<ProviderConfig> configs);

        Task<AppPreferences> LoadPreferencesAsync();

        Task SavePreferencesAsync(AppPreferences preferences);
    }
}
