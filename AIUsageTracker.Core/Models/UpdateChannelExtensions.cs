// <copyright file="UpdateChannelExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public static class UpdateChannelExtensions
{
    public static string ToAppcastSuffix(this UpdateChannel channel)
    {
        return channel switch
        {
            UpdateChannel.Stable => string.Empty,
            UpdateChannel.Beta => "_beta",
            _ => string.Empty,
        };
    }

    public static string ToDisplayName(this UpdateChannel channel)
    {
        return channel switch
        {
            UpdateChannel.Stable => "Stable",
            UpdateChannel.Beta => "Beta",
            _ => "Stable",
        };
    }
}
