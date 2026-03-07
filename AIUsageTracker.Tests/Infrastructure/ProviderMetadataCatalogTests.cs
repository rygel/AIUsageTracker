using System.Reflection;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure;

public class ProviderMetadataCatalogTests
{
    [Fact]
    public void Definitions_AreDiscoveredFromProviderClasses()
    {
        var expectedProviderIds = typeof(ProviderMetadataCatalog).Assembly
            .GetTypes()
            .Where(type =>
                type.IsClass &&
                !type.IsAbstract &&
                typeof(IProviderService).IsAssignableFrom(type))
            .Select(type => type.GetProperty(
                "StaticDefinition",
                BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Select(property => Assert.IsType<ProviderDefinition>(property?.GetValue(null)))
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var actualProviderIds = ProviderMetadataCatalog.Definitions
            .Select(definition => definition.ProviderId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.Equal(expectedProviderIds, actualProviderIds);
    }

    [Theory]
    [InlineData("codex.spark", "codex", "OpenAI (GPT-5.3-Codex-Spark)")]
    [InlineData("gemini", "gemini-cli", "Google Gemini")]
    [InlineData("kimi-for-coding", "kimi", "Kimi")]
    [InlineData("minimax-io", "minimax", "Minimax (International)")]
    [InlineData("minimax-global", "minimax", "Minimax (International)")]
    [InlineData("zai", "zai-coding-plan", "Z.AI")]
    public void Find_UsesProviderDefinitionsForAliases(string providerId, string expectedDefinitionId, string expectedDisplayName)
    {
        var definition = ProviderMetadataCatalog.Find(providerId);

        Assert.NotNull(definition);
        Assert.Equal(expectedDefinitionId, definition!.ProviderId);
        Assert.Equal(expectedDisplayName, ProviderMetadataCatalog.GetDisplayName(providerId));
    }

    [Fact]
    public void Definitions_DoNotExposeDuplicateHandledProviderIds()
    {
        var duplicateHandledIds = ProviderMetadataCatalog.Definitions
            .SelectMany(definition => definition.HandledProviderIds.Select(handledId => new
            {
                HandledId = handledId,
                definition.ProviderId
            }))
            .GroupBy(item => item.HandledId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Select(item => item.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicateHandledIds);
    }

    [Fact]
    public void TryCreateDefaultConfig_UsesDefinitionDefaults()
    {
        var created = ProviderMetadataCatalog.TryCreateDefaultConfig(
            "codex.spark",
            out var config,
            apiKey: "token",
            authSource: "test",
            description: "demo");

        Assert.True(created);
        Assert.Equal("codex.spark", config.ProviderId);
        Assert.Equal("quota-based", config.Type);
        Assert.Equal(PlanType.Coding, config.PlanType);
        Assert.Equal("token", config.ApiKey);
        Assert.Equal("test", config.AuthSource);
        Assert.Equal("demo", config.Description);
    }

    [Theory]
    [InlineData("antigravity", true)]
    [InlineData("antigravity.some-model", true)]
    [InlineData("codex", false)]
    [InlineData("openrouter", false)]
    public void IsAutoIncluded_UsesProviderDefinitions(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.IsAutoIncluded(providerId));
    }

    [Theory]
    [InlineData("codex.spark", "codex")]
    [InlineData("antigravity.claude-opus", "antigravity")]
    [InlineData("kimi-for-coding", "kimi")]
    [InlineData("minimax-io", "minimax")]
    [InlineData("unknown-provider", "unknown-provider")]
    public void GetCanonicalProviderId_UsesProviderDefinitions(string providerId, string expectedCanonicalId)
    {
        Assert.Equal(expectedCanonicalId, ProviderMetadataCatalog.GetCanonicalProviderId(providerId));
    }

    [Theory]
    [InlineData("antigravity", true)]
    [InlineData("antigravity.some-model", false)]
    [InlineData("codex", false)]
    public void IsAggregateParentProviderId_DetectsOnlyAggregateParent(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.IsAggregateParentProviderId(providerId));
    }

    [Theory]
    [InlineData("OPENAI_API_KEY", "openai")]
    [InlineData("CODEX_API_KEY", "codex")]
    [InlineData("MOONSHOT_API_KEY", "kimi")]
    [InlineData("CLAUDE_API_KEY", "claude-code")]
    public void FindByEnvironmentVariable_UsesProviderDefinitions(string environmentVariableName, string expectedProviderId)
    {
        var definition = ProviderMetadataCatalog.FindByEnvironmentVariable(environmentVariableName);

        Assert.NotNull(definition);
        Assert.Equal(expectedProviderId, definition!.ProviderId);
    }

    [Theory]
    [InlineData("openAiApiKey", "openai")]
    [InlineData("geminiApiKey", "gemini-cli")]
    [InlineData("openrouterApiKey", "openrouter")]
    [InlineData("mistralApiKey", "mistral")]
    public void FindByRooConfigProperty_UsesProviderDefinitions(string propertyName, string expectedProviderId)
    {
        var definition = ProviderMetadataCatalog.FindByRooConfigProperty(propertyName);

        Assert.NotNull(definition);
        Assert.Equal(expectedProviderId, definition!.ProviderId);
    }

    [Theory]
    [InlineData("codex.spark", false)]
    [InlineData("codex", true)]
    [InlineData("openai", true)]
    public void ShouldPersistProviderId_UsesProviderDefinitions(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.ShouldPersistProviderId(providerId));
    }

    [Theory]
    [InlineData("codex.spark", true)]
    [InlineData("codex", false)]
    [InlineData("openai", false)]
    public void IsVisibleDerivedProviderId_UsesProviderDefinitions(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.IsVisibleDerivedProviderId(providerId));
    }

    [Fact]
    public void GetStartupRefreshProviderIds_UsesProviderDefinitions()
    {
        var providerIds = ProviderMetadataCatalog.GetStartupRefreshProviderIds();

        Assert.Contains("antigravity", providerIds);
        Assert.DoesNotContain("codex", providerIds);
    }

    [Fact]
    public void Find_ExposesClaudeSessionAuthDiscoveryMetadata()
    {
        var definition = ProviderMetadataCatalog.Find("claude-code");

        Assert.NotNull(definition);
        Assert.Contains("%USERPROFILE%\\.claude\\.credentials.json", definition!.AuthIdentityCandidatePathTemplates);
        Assert.Contains(definition.SessionAuthFileSchemas, schema =>
            schema.RootProperty == "claudeAiOauth" &&
            schema.AccessTokenProperty == "accessToken");
    }

    [Fact]
    public void ShouldSuppressUsageProviderId_ReturnsTrue_ForSessionBackedAliasWithCanonicalConfig()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
            new() { ProviderId = "openai", ApiKey = "legacy-session-token" }
        };

        var result = ProviderMetadataCatalog.ShouldSuppressUsageProviderId(configs, "openai");

        Assert.True(result);
    }

    [Fact]
    public void ShouldSuppressUsageProviderId_ReturnsTrue_ForStaleSessionBackedAliasHistory_WhenCanonicalConfigExists()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" }
        };

        var result = ProviderMetadataCatalog.ShouldSuppressUsageProviderId(configs, "openai");

        Assert.True(result);
    }

    [Fact]
    public void ShouldSuppressUsageProviderId_ReturnsFalse_ForExplicitAliasApiKeyConfig()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" },
            new() { ProviderId = "openai", ApiKey = "sk-live-openai" }
        };

        var result = ProviderMetadataCatalog.ShouldSuppressUsageProviderId(configs, "openai");

        Assert.False(result);
    }

    [Fact]
    public void UsageFilter_RemovesStaleSessionAliasUsage_WhenCanonicalProviderIsConfigured()
    {
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "codex-session" }
        };
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", ProviderName = "OpenAI (Codex)" },
            new() { ProviderId = "openai", ProviderName = "OpenAI" }
        };

        var visible = usages
            .Where(usage => !ProviderMetadataCatalog.ShouldSuppressUsageProviderId(configs, usage.ProviderId))
            .ToList();

        Assert.Single(visible);
        Assert.Equal("codex", visible[0].ProviderId);
    }
}
