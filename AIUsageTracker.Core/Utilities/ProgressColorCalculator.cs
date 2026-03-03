namespace AIUsageTracker.Core.Utilities;

public static class ProgressColorCalculator
{
    public const double YellowThreshold = 70.0;
    public const double RedThreshold = 90.0;
    public const double HighThreshold = 90.0;
    public const double MediumThreshold = 50.0;

    public static ProgressBarColor GetColor(double usagePercentage)
    {
        if (usagePercentage >= RedThreshold)
        {
            return ProgressBarColor.Red;
        }

        if (usagePercentage >= YellowThreshold)
        {
            return ProgressBarColor.Yellow;
        }

        return ProgressBarColor.Green;
    }

    public static string GetColorClass(double usagePercentage)
    {
        return GetColor(usagePercentage) switch
        {
            ProgressBarColor.Red => "progress-bar-red",
            ProgressBarColor.Yellow => "progress-bar-yellow",
            ProgressBarColor.Green => "progress-bar-green"
        };
    }

    public enum ProgressBarColor
    {
        Green,
        Yellow,
        Red
    }
}
