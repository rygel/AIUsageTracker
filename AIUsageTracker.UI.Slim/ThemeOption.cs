using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed class ThemeOption
{
    public AppTheme Value { get; init; }

    public string Label { get; init; } = string.Empty;
}
