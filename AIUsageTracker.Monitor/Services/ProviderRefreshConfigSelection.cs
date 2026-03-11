// <copyright file="ProviderRefreshConfigSelection.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

internal sealed record ProviderRefreshConfigSelection(
    List<ProviderConfig> ActiveConfigs,
    int SuppressedConfigCount);
