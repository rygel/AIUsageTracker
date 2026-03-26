// <copyright file="IPreferencesStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IPreferencesStore
{
    Task<AppPreferences> LoadAsync();

    Task<bool> SaveAsync(AppPreferences preferences);
}
