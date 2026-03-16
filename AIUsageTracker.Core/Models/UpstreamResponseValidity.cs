// <copyright file="UpstreamResponseValidity.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public enum UpstreamResponseValidity
{
    Unknown = 0,
    NotAttempted = 1,
    Valid = 2,
    Invalid = 3,
}
