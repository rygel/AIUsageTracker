// <copyright file="ProviderMetadataCatalogTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
    [InlineData("opencode-go", "opencode-zen", "Opencode Go")]
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
                definition.ProviderId,
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
    [InlineData("github-copilot", true)]
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
    [InlineData("opencode-go", "opencode-zen")]
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
    [InlineData("antigravity", true)]
    [InlineData("antigravity.some-model", true)]
    [InlineData("codex", false)]
    [InlineData("codex.spark", false)]
    public void ShouldCollapseDerivedChildrenInMainWindow_UsesProviderDefinitions(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.ShouldCollapseDerivedChildrenInMainWindow(providerId));
    }

    [Theory]
    [InlineData("codex", true)]
    [InlineData("codex.spark", true)]
    [InlineData("antigravity.some-model", true)]
    [InlineData("unknown-provider", false)]
    public void ShouldShowInMainWindow_UsesCatalogVisibility(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.ShouldShowInMainWindow(providerId));
    }

    [Theory]
    [InlineData("antigravity", true)]
    [InlineData("antigravity.some-model", true)]
    [InlineData("codex", false)]
    public void ShouldRenderAggregateDetailsInMainWindow_UsesCatalogPolicy(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.ShouldRenderAggregateDetailsInMainWindow(providerId));
    }

    [Theory]
    [InlineData("antigravity", true)]
    [InlineData("antigravity.some-model", true)]
    [InlineData("codex", false)]
    public void ShouldUseSharedSubDetailCollapsePreference_UsesCatalogPolicy(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.ShouldUseSharedSubDetailCollapsePreference(providerId));
    }

    [Theory]
    [InlineData("antigravity.some-model", true)]
    [InlineData("codex.spark", false)]
    [InlineData("codex", false)]
    public void ShouldRenderAsSettingsSubItem_UsesCatalogPolicy(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.ShouldRenderAsSettingsSubItem(providerId));
    }

    [Theory]
    [InlineData("OPENAI_API_KEY", "openai")]
    [InlineData("CODEX_API_KEY", "codex")]
    [InlineData("GEMINI_API_KEY", "gemini-cli")]
    [InlineData("DEEPSEEK_API_KEY", "deepseek")]
    [InlineData("MOONSHOT_API_KEY", "kimi")]
    [InlineData("ZAI_API_KEY", "zai-coding-plan")]
    [InlineData("SYNTHETIC_API_KEY", "synthetic")]
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
    [InlineData("deepseekApiKey", "deepseek")]
    [InlineData("zaiApiKey", "zai-coding-plan")]
    [InlineData("syntheticApiKey", "synthetic")]
    public void FindByRooConfigProperty_UsesProviderDefinitions(string propertyName, string expectedProviderId)
    {
        var definition = ProviderMetadataCatalog.FindByRooConfigProperty(propertyName);

        Assert.NotNull(definition);
        Assert.Equal(expectedProviderId, definition!.ProviderId);
    }

    [Theory]
    [InlineData("codex.spark", true)]
    [InlineData("codex", true)]
    [InlineData("openai", false)]
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

    [Theory]
    [InlineData("codex", true)]
    [InlineData("openai", false)]
    [InlineData("anthropic", false)]
    public void ShouldShowInSettings_UsesProviderDefinitions(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.ShouldShowInSettings(providerId));
    }

    [Fact]
    public void GetStartupRefreshProviderIds_UsesProviderDefinitions()
    {
        var providerIds = ProviderMetadataCatalog.GetStartupRefreshProviderIds();

        Assert.Contains("antigravity", providerIds);
        Assert.DoesNotContain("codex", providerIds);
    }

    [Fact]
    public void GetDefaultSettingsProviderIds_UsesProviderDefinitionAdditionalIds()
    {
        var providerIds = ProviderMetadataCatalog.GetDefaultSettingsProviderIds();

        Assert.Contains("codex.spark", providerIds);
        Assert.Contains("minimax", providerIds);
        Assert.Contains("minimax-io", providerIds);
        Assert.DoesNotContain("openai", providerIds);
    }

    [Fact]
    public void Definitions_DeclarePerProviderAuthFallbackContract()
    {
        var localRuntimeProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "antigravity",
            "opencode-zen",
        };
        var externalAuthProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "github-copilot",
        };

        foreach (var definition in ProviderMetadataCatalog.Definitions)
        {
            if (localRuntimeProviders.Contains(definition.ProviderId))
            {
                Assert.True(
                    definition.SettingsMode == ProviderSettingsMode.AutoDetectedStatus ||
                    definition.AutoIncludeWhenUnconfigured,
                    $"Provider '{definition.ProviderId}' must declare local runtime auth mode.");
                continue;
            }

            if (externalAuthProviders.Contains(definition.ProviderId))
            {
                Assert.Equal(ProviderSettingsMode.ExternalAuthStatus, definition.SettingsMode);
                Assert.NotEmpty(definition.AuthIdentityCandidatePathTemplates);
                continue;
            }

            var hasConfigFallback =
                definition.DiscoveryEnvironmentVariables.Count > 0 ||
                definition.RooConfigPropertyNames.Count > 0 ||
                definition.AuthIdentityCandidatePathTemplates.Count > 0;

            Assert.True(
                hasConfigFallback,
                $"Provider '{definition.ProviderId}' has no declared auth fallback sources.");
        }
    }

    [Fact]
    public void Definitions_EnforceCriticalProviderFallbackMappings()
    {
        var codex = ProviderMetadataCatalog.Find("codex");
        Assert.NotNull(codex);
        Assert.Contains("CODEX_API_KEY", codex!.DiscoveryEnvironmentVariables);
        Assert.Contains("%USERPROFILE%\\.codex\\auth.json", codex.AuthIdentityCandidatePathTemplates);
        Assert.Contains("%APPDATA%\\codex\\auth.json", codex.AuthIdentityCandidatePathTemplates);
        Assert.Contains(
            codex.SessionAuthFileSchemas,
            schema => string.Equals(schema.RootProperty, "tokens", StringComparison.Ordinal) &&
                      string.Equals(schema.AccessTokenProperty, "access_token", StringComparison.Ordinal));

        var gemini = ProviderMetadataCatalog.Find("gemini-cli");
        Assert.NotNull(gemini);
        Assert.Contains("GEMINI_API_KEY", gemini!.DiscoveryEnvironmentVariables);
        Assert.Contains("GOOGLE_API_KEY", gemini.DiscoveryEnvironmentVariables);
        Assert.Contains("geminiApiKey", gemini.RooConfigPropertyNames);

        var deepSeek = ProviderMetadataCatalog.Find("deepseek");
        Assert.NotNull(deepSeek);
        Assert.Contains("DEEPSEEK_API_KEY", deepSeek!.DiscoveryEnvironmentVariables);
        Assert.Contains("deepseekApiKey", deepSeek.RooConfigPropertyNames);

        var synthetic = ProviderMetadataCatalog.Find("synthetic");
        Assert.NotNull(synthetic);
        Assert.Contains("SYNTHETIC_API_KEY", synthetic!.DiscoveryEnvironmentVariables);
        Assert.Contains("syntheticApiKey", synthetic.RooConfigPropertyNames);

        var zai = ProviderMetadataCatalog.Find("zai-coding-plan");
        Assert.NotNull(zai);
        Assert.Contains("ZAI_API_KEY", zai!.DiscoveryEnvironmentVariables);
        Assert.Contains("Z_AI_API_KEY", zai.DiscoveryEnvironmentVariables);
        Assert.Contains("zaiApiKey", zai.RooConfigPropertyNames);
    }

    [Fact]
    public void Find_ExposesClaudeSessionAuthDiscoveryMetadata()
    {
        var definition = ProviderMetadataCatalog.Find("claude-code");

        Assert.NotNull(definition);
        Assert.Contains("%USERPROFILE%\\.claude\\.credentials.json", definition!.AuthIdentityCandidatePathTemplates);
        Assert.Contains(definition.SessionAuthFileSchemas, schema => string.Equals(schema.RootProperty, "claudeAiOauth", StringComparison.Ordinal) &&
string.Equals(schema.AccessTokenProperty, "accessToken", StringComparison.Ordinal));
    }

    [Fact]
    public void UsageFilter_RemovesNonPersistedProviderIds_UsingPersistenceGate()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", ProviderName = "OpenAI (Codex)" },
            new() { ProviderId = "openai", ProviderName = "OpenAI" },
        };

        var visible = usages
            .Where(usage => ProviderMetadataCatalog.ShouldPersistProviderId(usage.ProviderId))
            .ToList();

        Assert.Single(visible);
        Assert.Equal("codex", visible[0].ProviderId);
    }
}
