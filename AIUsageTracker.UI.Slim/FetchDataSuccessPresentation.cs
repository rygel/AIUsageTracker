// <copyright file="FetchDataSuccessPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record FetchDataSuccessPresentation(
    string StatusMessage,
    bool SwitchToNormalInterval);
