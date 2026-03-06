using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Infrastructure.Configuration;

public static class ProviderConfigNormalizer
{
    public static void NormalizeOpenAiCodexSessionOverlap(List<ProviderConfig> configs)
    {
        var openAiConfig = configs.FirstOrDefault(c => c.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase));
        if (openAiConfig == null)
        {
            return;
        }

        var openAiHasApiKey = !string.IsNullOrWhiteSpace(openAiConfig.ApiKey);
        var openAiHasExplicitApiKey = openAiHasApiKey &&
                                      openAiConfig.ApiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
        if (openAiHasExplicitApiKey)
        {
            return;
        }

        var codexConfig = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
        if (codexConfig == null)
        {
            if (!ProviderMetadataCatalog.TryCreateDefaultConfig("codex", out codexConfig))
            {
                return;
            }

            configs.Add(codexConfig);
        }

        if (string.IsNullOrWhiteSpace(codexConfig.ApiKey) && openAiHasApiKey)
        {
            codexConfig.ApiKey = openAiConfig.ApiKey;
            codexConfig.AuthSource = openAiConfig.AuthSource;
            codexConfig.Description = "Migrated from OpenAI session config";
        }

        configs.RemoveAll(c => c.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase));
    }

    public static void NormalizeCodexSparkConfiguration(List<ProviderConfig> configs)
    {
        var sparkConfigs = configs
            .Where(c => c.ProviderId.Equals("codex.spark", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sparkConfigs.Count == 0)
        {
            return;
        }

        var codexConfig = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
        if (codexConfig == null)
        {
            if (!ProviderMetadataCatalog.TryCreateDefaultConfig("codex", out codexConfig))
            {
                return;
            }

            configs.Add(codexConfig);
        }

        foreach (var sparkConfig in sparkConfigs)
        {
            if (string.IsNullOrWhiteSpace(codexConfig.ApiKey) && !string.IsNullOrWhiteSpace(sparkConfig.ApiKey))
            {
                codexConfig.ApiKey = sparkConfig.ApiKey;
            }

            if ((string.IsNullOrWhiteSpace(codexConfig.AuthSource) ||
                 string.Equals(codexConfig.AuthSource, "Unknown", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(sparkConfig.AuthSource))
            {
                codexConfig.AuthSource = sparkConfig.AuthSource;
            }

            if (string.IsNullOrWhiteSpace(codexConfig.Description) && !string.IsNullOrWhiteSpace(sparkConfig.Description))
            {
                codexConfig.Description = sparkConfig.Description;
            }

            if (string.IsNullOrWhiteSpace(codexConfig.BaseUrl) && !string.IsNullOrWhiteSpace(sparkConfig.BaseUrl))
            {
                codexConfig.BaseUrl = sparkConfig.BaseUrl;
            }

            codexConfig.ShowInTray = codexConfig.ShowInTray || sparkConfig.ShowInTray;
            codexConfig.EnableNotifications = codexConfig.EnableNotifications || sparkConfig.EnableNotifications;
        }

        configs.RemoveAll(c => c.ProviderId.Equals("codex.spark", StringComparison.OrdinalIgnoreCase));
    }
}
