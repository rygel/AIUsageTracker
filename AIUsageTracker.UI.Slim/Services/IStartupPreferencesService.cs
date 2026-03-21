// <copyright file="IStartupPreferencesService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Services;

public interface IStartupPreferencesService
{
    Task<AppPreferences> LoadAndApplyAsync();
}
