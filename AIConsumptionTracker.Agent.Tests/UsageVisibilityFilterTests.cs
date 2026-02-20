using AIConsumptionTracker.Agent.Services;
using AIConsumptionTracker.Core.Models;
using Xunit;

namespace AIConsumptionTracker.Agent.Tests;

public class UsageVisibilityFilterTests
{
    [Fact]
    public void FilterForConfiguredProviders_ExcludesProviderWithoutKey()
    {
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "kimi",
                ProviderName = "Kimi",
                RequestsPercentage = 75,
                IsAvailable = true
            }
        };

        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "kimi",
                ApiKey = ""
            }
        };

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterForConfiguredProviders_ExcludesStaleProviderNotInConfig()
    {
        var usages = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "kimi",
                ProviderName = "Kimi",
                RequestsPercentage = 80,
                IsAvailable = true
            }
        };

        var configs = new List<ProviderConfig>
        {
            new()
            {
                ProviderId = "openai",
                ApiKey = "key"
            }
        };

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterForConfiguredProviders_IncludesProviderWithKeyAndSystemProviders()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "openai", ProviderName = "OpenAI", RequestsPercentage = 30, IsAvailable = true },
            new() { ProviderId = "antigravity", ProviderName = "Antigravity", RequestsPercentage = 60, IsAvailable = true },
            new() { ProviderId = "antigravity.model-a", ProviderName = "Model A", RequestsPercentage = 50, IsAvailable = true },
            new() { ProviderId = "gemini-cli", ProviderName = "Gemini CLI", RequestsPercentage = 40, IsAvailable = true }
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "openai", ApiKey = "key" },
            new() { ProviderId = "kimi", ApiKey = "" }
        };

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs, hasGeminiCliAccountsOverride: true);
        var filteredIds = filtered.Select(u => u.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(4, filtered.Count);
        Assert.Contains("openai", filteredIds);
        Assert.Contains("antigravity", filteredIds);
        Assert.Contains("antigravity.model-a", filteredIds);
        Assert.Contains("gemini-cli", filteredIds);
    }

    [Fact]
    public void FilterForConfiguredProviders_ExcludesGeminiCliWhenNoActiveAccountDetected()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "gemini-cli", ProviderName = "Gemini CLI", RequestsPercentage = 50, IsAvailable = true }
        };

        var configs = new List<ProviderConfig>();

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs, hasGeminiCliAccountsOverride: false);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterForConfiguredProviders_ExcludesGeminiCliWhenUnavailable()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "gemini-cli", ProviderName = "Gemini CLI", RequestsPercentage = 50, IsAvailable = false }
        };

        var configs = new List<ProviderConfig>();

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs, hasGeminiCliAccountsOverride: true);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterForConfiguredProviders_ExcludesCodexWhenKeyMissing()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", ProviderName = "Codex", RequestsPercentage = 25, IsAvailable = true }
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "" }
        };

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterForConfiguredProviders_IncludesCodexWhenKeyPresent()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", ProviderName = "Codex", RequestsPercentage = 25, IsAvailable = true }
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "key" }
        };

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs);

        Assert.Single(filtered);
        Assert.Equal("codex", filtered[0].ProviderId);
    }

    [Fact]
    public void FilterForConfiguredProviders_ExcludesOpenCodeWhenUnavailable()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "opencode", ProviderName = "OpenCode", RequestsPercentage = 0, IsAvailable = false, Description = "Invalid API response (not JSON)" }
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "opencode", ApiKey = "key" }
        };

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs);

        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterForConfiguredProviders_IncludesOpenCodeWhenAvailable()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "opencode", ProviderName = "OpenCode", RequestsPercentage = 0, IsAvailable = true, Description = "$3.25 used (7 days)" }
        };

        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "opencode", ApiKey = "key" }
        };

        var filtered = UsageVisibilityFilter.FilterForConfiguredProviders(usages, configs);

        Assert.Single(filtered);
        Assert.Equal("opencode", filtered[0].ProviderId);
    }
}
