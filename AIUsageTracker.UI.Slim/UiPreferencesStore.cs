// <copyright file="UiPreferencesStore.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

/// <summary>
/// Desktop-specific preferences store. Delegates to the shared
/// <see cref="PreferencesStore"/> in Infrastructure.
/// </summary>
public class UiPreferencesStore : PreferencesStore, IUiPreferencesStore
{
    public UiPreferencesStore(ILogger<UiPreferencesStore> logger, IAppPathProvider pathProvider)
        : base(logger, pathProvider)
    {
    }
}
