// <copyright file="AppThemeService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Services;

public sealed class AppThemeService : IAppThemeService
{
    public void ApplyTheme(AppTheme theme)
    {
        App.ApplyTheme(theme);
    }
}
