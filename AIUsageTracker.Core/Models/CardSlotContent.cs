// <copyright file="CardSlotContent.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Defines what content to display in each configurable card slot.
/// </summary>
public enum CardSlotContent
{
    None,
    PaceBadge,
    ProjectedPercent,
    DailyBudget,
    UsageRate,
    UsedPercent,
    RemainingPercent,
    ResetAbsolute,
    ResetAbsoluteDate,
    ResetRelative,
    AccountName,
    StatusText,
    AuthSource,
}
