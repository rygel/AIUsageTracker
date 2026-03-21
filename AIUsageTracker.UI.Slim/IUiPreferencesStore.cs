// <copyright file="IUiPreferencesStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

public interface IUiPreferencesStore
{
    Task<AppPreferences> LoadAsync();

    Task<bool> SaveAsync(AppPreferences preferences);
}
