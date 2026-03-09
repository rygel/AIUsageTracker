// <copyright file="IConfigService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public interface IConfigService
{
    Task<List<ProviderConfig>> GetConfigsAsync();

    Task SaveConfigAsync(ProviderConfig config);

    Task RemoveConfigAsync(string providerId);

    Task<AppPreferences> GetPreferencesAsync();

    Task SavePreferencesAsync(AppPreferences preferences);

    Task<List<ProviderConfig>> ScanForKeysAsync();
}
