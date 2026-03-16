// <copyright file="ProviderInputMode.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal enum ProviderInputMode
{
    StandardApiKey,
    DerivedReadOnly,
    AutoDetectedStatus,
    ExternalAuthStatus,
    SessionAuthStatus,
}
