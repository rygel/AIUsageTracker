// <copyright file="IAppThemeService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim.Services;

public interface IAppThemeService
{
    void ApplyTheme(AppTheme theme);
}
