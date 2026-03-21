// <copyright file="StatusPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

internal sealed record StatusPresentation(
    string Message,
    StatusType Type,
    StatusIndicatorKind IndicatorKind,
    string TooltipText,
    LogLevel LogLevel);
