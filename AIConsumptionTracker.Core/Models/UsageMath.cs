namespace AIConsumptionTracker.Core.Models;

public static class UsageMath
{
    public static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0d, 100d);
    }

    public static double CalculateUsedPercent(double used, double total)
    {
        if (total <= 0)
        {
            return 0;
        }

        return ClampPercent((used / total) * 100d);
    }

    public static double CalculateRemainingPercent(double used, double total)
    {
        if (total <= 0)
        {
            return 100;
        }

        return ClampPercent(((total - used) / total) * 100d);
    }

    public static double GetEffectiveUsedPercent(ProviderUsage usage)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var percentage = ClampPercent(usage.RequestsPercentage);
        var isQuota = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
        return isQuota ? ClampPercent(100 - percentage) : percentage;
    }
}
