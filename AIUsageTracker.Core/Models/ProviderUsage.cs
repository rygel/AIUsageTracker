namespace AIUsageTracker.Core.Models;

public enum ProviderUsageDetailType
{
    Unknown = 0,
    QuotaWindow = 1,
    Credit = 2,
    Model = 3,
    Other = 4
}

public enum WindowKind
{
    None = 0,
    Primary = 1,
    Secondary = 2,
    Spark = 3
}

public class ProviderUsage
{
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public double RequestsUsed { get; set; }
    public double RequestsAvailable { get; set; }
    public double RequestsPercentage { get; set; }
    public PlanType PlanType { get; set; } = PlanType.Usage;

    /// <summary>
    /// Whether the provider is available and functioning correctly.
    /// 
    /// Semantics:
    /// - true = Successfully fetched data OR soft failure with valid credentials
    /// - false = Authentication failure, invalid key, or complete unavailability
    /// </summary>
    public bool IsAvailable { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string AuthSource { get; set; } = string.Empty;
    public List<ProviderUsageDetail>? Details { get; set; }
    
    // Temporary property for database serialization - not serialized to JSON
    [System.Text.Json.Serialization.JsonIgnore]
    public string? DetailsJson { get; set; }
    
    public string AccountName { get; set; } = string.Empty;
    public string ConfigKey { get; set; } = string.Empty;
    public DateTime? NextResetTime { get; set; }
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
    public double ResponseLatencyMs { get; set; }
    public string? RawJson { get; set; }
    public int HttpStatus { get; set; } = 200;

    public string GetFriendlyName()
    {
        // Straight Line Architecture: Prefer the name provided by the Provider Class
        if (!string.IsNullOrWhiteSpace(ProviderName) && 
            !string.Equals(ProviderName, ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return ProviderName;
        }

        if (string.IsNullOrWhiteSpace(ProviderId))
        {
            return "Unknown Provider";
        }

        // Clean fallback: TitleCase the ID (e.g. "github-copilot" -> "Github Copilot")
        var name = ProviderId.Replace("_", " ").Replace("-", " ");
        
        // Handle child IDs (e.g. "codex.primary" -> "Codex Primary")
        if (name.Contains('.'))
        {
            name = name.Replace(".", " ");
        }

        return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
    }

    public string GetRelativeResetTime()
    {
        if (!NextResetTime.HasValue) return string.Empty;
        
        var diff = NextResetTime.Value - DateTime.Now;

        if (diff.TotalSeconds <= 0) return "0m";
        if (diff.TotalDays >= 1) return $"{diff.Days}d {diff.Hours}h";
        if (diff.TotalHours >= 1) return $"{diff.Hours}h {diff.Minutes}m";
        return $"{Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes))}m";
    }

    public string GetStatusText(bool showUsed)
    {
        var isUnknown = !IsAvailable && string.Equals(Description, "Usage unknown", StringComparison.OrdinalIgnoreCase);
        
        // Status-only providers
        if (string.Equals(UsageUnit, "Status", StringComparison.OrdinalIgnoreCase))
        {
            return Description ?? string.Empty;
        }

        if (isUnknown)
        {
            return Description ?? "Usage unknown";
        }

        if (!IsAvailable)
        {
            return Description ?? "Unavailable";
        }

        if (IsQuotaBased)
        {
            // Check if we have raw numbers (limit > 100 serves as a heuristic for usage limits > 100%)
            if (DisplayAsFraction)
            {
                if (showUsed)
                {
                    return $"{RequestsUsed:N0} / {RequestsAvailable:N0} used";
                }
                else
                {
                    var remaining = RequestsAvailable - RequestsUsed;
                    return $"{remaining:N0} / {RequestsAvailable:N0} remaining";
                }
            }
            else
            {
                // Percentage only mode
                var remainingPercent = UsageMath.ClampPercent(RequestsPercentage);
                if (showUsed)
                {
                    return $"{(100.0 - remainingPercent):F0}% used";
                }
                else
                {
                    return $"{remainingPercent:F0}% remaining";
                }
            }
        }
        else if (PlanType == PlanType.Usage && RequestsAvailable > 0)
        {
            var usedPercent = UsageMath.ClampPercent(RequestsPercentage);
            return showUsed
                ? $"{usedPercent:F0}% used"
                : $"{(100.0 - usedPercent):F0}% remaining";
        }

        return Description ?? string.Empty;
    }
}

public class ProviderUsageDetail
{
    public string Name { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string Used { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime? NextResetTime { get; set; }
    public ProviderUsageDetailType DetailType { get; set; } = ProviderUsageDetailType.Unknown;
    public WindowKind WindowKind { get; set; } = WindowKind.None;

    public bool IsPrimaryQuotaDetail()
    {
        return DetailType == ProviderUsageDetailType.QuotaWindow && WindowKind == WindowKind.Primary;
    }

    public bool IsSecondaryQuotaDetail()
    {
        return DetailType == ProviderUsageDetailType.QuotaWindow && WindowKind == WindowKind.Secondary;
    }

    public bool IsWindowQuotaDetail()
    {
        return DetailType == ProviderUsageDetailType.QuotaWindow;
    }

    public bool IsCreditDetail()
    {
        return DetailType == ProviderUsageDetailType.Credit;
    }

    public bool IsDisplayableSubProviderDetail()
    {
        return DetailType == ProviderUsageDetailType.Model || DetailType == ProviderUsageDetailType.Other;
    }
}

