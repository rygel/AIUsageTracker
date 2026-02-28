namespace AIUsageTracker.Core.Models;

public enum UpdateChannel
{
    Stable,
    Beta
}

public static class UpdateChannelExtensions
{
    public static string ToAppcastSuffix(this UpdateChannel channel)
    {
        return channel switch
        {
            UpdateChannel.Stable => "",
            UpdateChannel.Beta => "_beta",
            _ => ""
        };
    }

    public static string ToDisplayName(this UpdateChannel channel)
    {
        return channel switch
        {
            UpdateChannel.Stable => "Stable",
            UpdateChannel.Beta => "Beta",
            _ => "Stable"
        };
    }
}
