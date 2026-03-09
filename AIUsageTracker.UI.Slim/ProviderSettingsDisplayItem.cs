// <copyright file="ProviderSettingsDisplayItem.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderSettingsDisplayItem(ProviderConfig Config, bool IsDerived);
