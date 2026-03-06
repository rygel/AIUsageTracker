using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal sealed class SettingsWindowDeterministicFixtureData
{
    public required List<ProviderConfig> Configs { get; init; }
    public required List<ProviderUsage> Usages { get; init; }
    public required IReadOnlyList<SettingsWindowHistoryRow> HistoryRows { get; init; }
    public string MonitorStatusText { get; init; } = "Running";
    public string MonitorPortText { get; init; } = "5000";
    public string MonitorLogsText { get; init; } =
        "Monitor health check: OK" + Environment.NewLine +
        "Diagnostics available in Settings > Monitor.";
}

internal sealed class SettingsWindowHistoryRow
{
    public string ProviderName { get; init; } = string.Empty;
    public double UsagePercentage { get; init; }
    public double Used { get; init; }
    public double Limit { get; init; }
    public string PlanType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public DateTime FetchedAt { get; init; }
}

internal static class SettingsWindowDeterministicFixture
{
    public static SettingsWindowDeterministicFixtureData Create()
    {
        var deterministicNow = new DateTime(2026, 02, 01, 12, 00, 00, DateTimeKind.Local);

        var configSeeds = new (string ProviderId, string ApiKey, bool ShowInTray, bool EnableNotifications)[]
        {
            ("antigravity", "local-session", false, false),
            ("claude-code", "cc-demo-key", true, false),
            ("deepseek", "sk-ds-demo", false, false),
            ("gemini-cli", "gemini-local-auth", false, false),
            ("github-copilot", "ghp_demo_key", true, true),
            ("kimi", "kimi-demo-key", false, false),
            ("minimax", "mm-cn-demo", false, false),
            ("minimax-io", "mm-intl-demo", false, false),
            ("mistral", "mistral-demo-key", false, false),
            ("openai", "sk-openai-demo", true, false),
            ("opencode", "oc-demo-key", false, false),
            ("opencode-zen", "ocz-demo-key", false, false),
            ("openrouter", "or-demo-key", false, false),
            ("synthetic", "syn-demo-key", false, false),
            ("zai-coding-plan", "zai-demo-key", true, false)
        };

        var usageSeeds = new (string ProviderId, double RequestsPercentage, string Description, int? ResetHours)[]
        {
            ("gemini-cli", 84.0, "84.0% Remaining", 12),
            ("github-copilot", 72.5, "72.5% Remaining", 20),
            ("kimi", 66.0, "66.0% Remaining", 9),
            ("minimax", 61.0, "61.0% Remaining", 11),
            ("openai", 63.0, "63.0% Remaining", 18),
            ("synthetic", 79.0, "79.0% Remaining", 4),
            ("zai-coding-plan", 88.0, "88.0% Remaining", 15)
        };

        var connectedUsageProviderIds = new[]
        {
            "claude-code",
            "deepseek",
            "minimax-io",
            "mistral",
            "opencode",
            "opencode-zen",
            "openrouter"
        };

        var configs = configSeeds
            .Select(seed => CreateConfig(seed.ProviderId, seed.ApiKey, seed.ShowInTray, seed.EnableNotifications))
            .ToList();

        var usages = new List<ProviderUsage>
        {
            CreateUsage(
                "antigravity",
                requestsPercentage: 60.0,
                description: "60.0% Remaining",
                nextResetTime: deterministicNow.AddHours(6),
                details: new List<ProviderUsageDetail>
                {
                    CreateDetail(deterministicNow, "Claude Opus 4.6 (Thinking)", "60%", 10),
                    CreateDetail(deterministicNow, "Claude Sonnet 4.6 (Thinking)", "60%", 10),
                    CreateDetail(deterministicNow, "Gemini 3 Flash", "100%", 6),
                    CreateDetail(deterministicNow, "Gemini 3.1 Pro (High)", "100%", 14),
                    CreateDetail(deterministicNow, "Gemini 3.1 Pro (Low)", "100%", 14),
                    CreateDetail(deterministicNow, "GPT-OSS 120B (Medium)", "60%", 8)
                })
        };

        usages.AddRange(connectedUsageProviderIds.Select(providerId => CreateUsage(providerId)));
        usages.AddRange(usageSeeds.Select(seed => CreateUsage(
            seed.ProviderId,
            requestsPercentage: seed.RequestsPercentage,
            description: seed.Description,
            nextResetTime: seed.ResetHours.HasValue ? deterministicNow.AddHours(seed.ResetHours.Value) : null)));

        var historyRows = new[]
        {
            CreateHistoryRow("github-copilot", 27.5, 27.5, 100.0, "Coding", "72.5% Remaining", new DateTime(2026, 2, 1, 12, 0, 0)),
            CreateHistoryRow("openai", 31.1, 12.45, 40.0, "Usage", "$12.45 / $40.00", new DateTime(2026, 2, 1, 12, 5, 0))
        };

        return new SettingsWindowDeterministicFixtureData
        {
            Configs = configs,
            Usages = usages,
            HistoryRows = historyRows
        };
    }

    private static ProviderConfig CreateConfig(string providerId, string apiKey, bool showInTray, bool enableNotifications)
    {
        if (!ProviderMetadataCatalog.TryCreateDefaultConfig(providerId, out var config, apiKey: apiKey))
        {
            throw new InvalidOperationException($"Unknown provider id '{providerId}' in deterministic screenshot data.");
        }

        config.ShowInTray = showInTray;
        config.EnableNotifications = enableNotifications;
        return config;
    }

    private static ProviderUsage CreateUsage(
        string providerId,
        double requestsPercentage = 0,
        double requestsUsed = 0,
        double requestsAvailable = 0,
        string description = "Connected",
        bool isAvailable = true,
        DateTime? nextResetTime = null,
        List<ProviderUsageDetail>? details = null)
    {
        var definition = ProviderMetadataCatalog.Find(providerId)
            ?? throw new InvalidOperationException($"Unknown provider id '{providerId}' in deterministic screenshot data.");

        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = ProviderMetadataCatalog.GetDisplayName(providerId),
            IsAvailable = isAvailable,
            IsQuotaBased = definition.IsQuotaBased,
            PlanType = definition.PlanType,
            RequestsPercentage = requestsPercentage,
            RequestsUsed = requestsUsed,
            RequestsAvailable = requestsAvailable,
            Description = description,
            Details = details,
            NextResetTime = nextResetTime
        };
    }

    private static ProviderUsageDetail CreateDetail(DateTime deterministicNow, string name, string used, int resetHours)
    {
        return new ProviderUsageDetail
        {
            Name = name,
            ModelName = name,
            GroupName = "Recommended Group 1",
            Used = used,
            Description = $"{used} remaining",
            NextResetTime = deterministicNow.AddHours(resetHours)
        };
    }

    private static SettingsWindowHistoryRow CreateHistoryRow(
        string providerId,
        double usagePercentage,
        double used,
        double limit,
        string planType,
        string description,
        DateTime fetchedAt)
    {
        return new SettingsWindowHistoryRow
        {
            ProviderName = ProviderMetadataCatalog.GetDisplayName(providerId),
            UsagePercentage = usagePercentage,
            Used = used,
            Limit = limit,
            PlanType = planType,
            Description = description,
            FetchedAt = fetchedAt
        };
    }
}
