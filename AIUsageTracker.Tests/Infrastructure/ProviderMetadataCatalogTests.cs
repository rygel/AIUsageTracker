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
    [InlineData("codex.spark", "codex", "OpenAI (GPT-5.3 Codex Spark)")]
    [InlineData("gemini", "gemini-cli", "Google Gemini")]
    [InlineData("gemini-cli.hourly", "gemini-cli", "Gemini CLI (Hourly)")]
    [InlineData("gemini-cli.daily", "gemini-cli", "Gemini CLI (Daily)")]
    [InlineData("kimi", "kimi-for-coding", "Kimi for Coding")]
    [InlineData("minimax-io", "minimax", "Minimax (International)")]
    [InlineData("minimax-global", "minimax", "Minimax (International)")]
    [InlineData("opencode-go", "opencode-zen", "Opencode Go")]
    [InlineData("zai", "zai-coding-plan", "Z.AI")]
    public void Find_UsesProviderDefinitionsForAliases(string providerId, string expectedDefinitionId, string expectedDisplayName)
    {
        var definition = ProviderMetadataCatalog.Find(providerId);

        Assert.NotNull(definition);
        Assert.Equal(expectedDefinitionId, definition!.ProviderId);
        Assert.Equal(expectedDisplayName, ProviderMetadataCatalog.ResolveDisplayLabel(providerId));
    }

    [Theory]
    [InlineData("codex", "OpenAI (Codex)", "OpenAI (Codex)")]
    [InlineData("openai", "OpenAI (API)", "OpenAI (API)")]
    public void ProviderLabels_UseDefinitionAuthority(
        string providerId,
        string expectedDisplayName,
        string expectedSessionLabel)
    {
        Assert.Equal(expectedDisplayName, ProviderMetadataCatalog.ResolveDisplayLabel(providerId));
        Assert.Equal(expectedSessionLabel, ProviderMetadataCatalog.Find(providerId)?.SessionStatusLabel);
    }

    [Theory]
    [InlineData("codex.spark", "OpenAI (GPT-5.3 Codex Spark)")]
    [InlineData("antigravity.gpt-oss", "Google Antigravity")]
    public void GetConfiguredDisplayName_UsesMetadataAuthority(string providerId, string expectedDisplayName)
    {
        Assert.Equal(expectedDisplayName, ProviderMetadataCatalog.GetConfiguredDisplayName(providerId));
    }

    [Theory]
    [InlineData("antigravity.gpt-oss", "GPT OSS (Anti-Gravity)", "GPT OSS (Anti-Gravity)")]
    [InlineData("gemini-cli.minute", "Gemini 2.5 Flash Lite [Gemini CLI]", "Gemini 2.5 Flash Lite [Gemini CLI]")]
    public void ResolveDisplayLabel_PreservesIntentionalRuntimeLabels_ForDynamicChildren(
        string providerId,
        string runtimeLabel,
        string expectedDisplayLabel)
    {
        Assert.Equal(expectedDisplayLabel, ProviderMetadataCatalog.ResolveDisplayLabel(providerId, runtimeLabel));
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
        Assert.Equal(expected, ProviderMetadataCatalog.Find(providerId)?.AutoIncludeWhenUnconfigured ?? false);
    }

    [Theory]
    [InlineData("codex.spark", "codex")]
    [InlineData("antigravity.claude-opus", "antigravity")]
    [InlineData("kimi", "kimi-for-coding")]
    [InlineData("minimax-io", "minimax")]
    [InlineData("opencode-go", "opencode-zen")]
    [InlineData("unknown-provider", "unknown-provider")]
    public void GetCanonicalProviderId_UsesProviderDefinitions(string providerId, string expectedCanonicalId)
    {
        Assert.Equal(expectedCanonicalId, ProviderMetadataCatalog.GetCanonicalProviderId(providerId));
    }

    [Theory]
    [InlineData("gemini", "gemini-cli.hourly", true)]
    [InlineData("codex", "codex.spark", true)]
    [InlineData("antigravity", "antigravity.gemini-3-flash", true)]
    [InlineData("openai", "openai.spark", false)]
    [InlineData("unknown-provider", "unknown-provider.child", false)]
    public void BelongsToProviderFamily_UsesProviderDefinitions(string providerId, string candidateProviderId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.Find(providerId)?.HandlesProviderId(candidateProviderId) ?? false);
    }

    [Theory]
    [InlineData("gemini-cli.hourly", true)]
    [InlineData("codex.spark", true)]
    [InlineData("antigravity.gemini-3-flash", true)]
    [InlineData("gemini", false)]
    [InlineData("openai.spark", false)]
    [InlineData("unknown-provider.child", false)]
    public void IsChildProviderId_UsesProviderDefinitions(string providerId, bool expected)
    {
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
        Assert.Equal(expected, ProviderMetadataCatalog.Find(canonicalProviderId)?.IsChildProviderId(providerId) ?? false);
    }

    [Theory]
    [InlineData("antigravity", "antigravity.gemini-3-flash", true, "gemini-3-flash")]
    [InlineData("gemini", "gemini-cli.hourly", true, "hourly")]
    [InlineData("codex", "codex.spark", true, "spark")]
    [InlineData("openai", "openai.spark", false, "")]
    public void TryGetChildProviderKey_UsesProviderDefinitions(
        string providerId,
        string candidateProviderId,
        bool expected,
        string expectedChildProviderKey)
    {
        var childProviderKey = string.Empty;
        var success = ProviderMetadataCatalog.Find(providerId)?.TryGetChildProviderKey(candidateProviderId, out childProviderKey) ?? false;

        Assert.Equal(expected, success);
        Assert.Equal(expectedChildProviderKey, childProviderKey);
    }

    [Fact]
    public void GetDerivedModelDisplayName_AppendsConfiguredSuffix()
    {
        var name = ProviderMetadataCatalog.GetDerivedModelDisplayName("gemini-cli", "Gemini 2.5 Flash Lite");

        Assert.Equal("Gemini 2.5 Flash Lite [Gemini CLI]", name);
    }

    [Fact]
    public void GetDerivedModelDisplayName_LeavesNameUnchanged_WhenNoSuffixConfigured()
    {
        var name = ProviderMetadataCatalog.GetDerivedModelDisplayName("codex", "GPT-5.3 Codex");

        Assert.Equal("GPT-5.3 Codex", name);
    }

    [Theory]
    [InlineData("codex", true)]
    [InlineData("codex.spark", true)]
    [InlineData("antigravity.some-model", true)]
    [InlineData("openai", false)]
    [InlineData("unknown-provider", false)]
    public void ShouldShowInMainWindow_UsesCatalogVisibility(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.Find(providerId)?.ShowInMainWindow ?? false);
    }

    [Theory]
    [InlineData("antigravity", true)]
    [InlineData("gemini-cli", true)]
    [InlineData("codex", true)]
    [InlineData("github-copilot", false)]
    public void HasDisplayableDerivedProviders_UsesProviderFamilyPolicy(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.Find(providerId)?.HasDisplayableDerivedProviders ?? false);
    }

    [Theory]
    [InlineData("antigravity", true)]
    [InlineData("gemini-cli", false)]
    [InlineData("codex", false)]
    [InlineData("github-copilot", false)]
    public void ShouldUseChildProviderRowsForGroupedModels_UsesProviderFamilyMode(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.Find(providerId)?.UseChildProviderRowsForGroupedModels ?? false);
    }

    [Theory]
    [InlineData("codex", new[] { "codex.spark" })]
    [InlineData("gemini-cli", new[] { "gemini-cli.minute", "gemini-cli.hourly", "gemini-cli.daily" })]
    [InlineData("antigravity", new string[0])]
    public void GetVisibleDerivedProviderIds_UsesProviderDefinitions(string providerId, string[] expected)
    {
        var providerIds = ProviderMetadataCatalog.Find(providerId)?.VisibleDerivedProviderIds ?? (IReadOnlyCollection<string>)Array.Empty<string>();

        Assert.Equal(expected, providerIds, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("codex", 0, 0)]
    [InlineData("gemini-cli", 0, 0)]
    [InlineData("antigravity", 0, 0)]
    public void DerivedModelSelectorCoverageQueries_ReportConfiguredProviders(
        string providerId,
        int expectedMissingCount,
        int expectedUnknownCount)
    {
        Assert.Equal(expectedMissingCount, GetMissingDerivedModelSelectorProviderIds(providerId).Count);
        Assert.Equal(expectedUnknownCount, GetUnknownDerivedModelSelectorProviderIds(providerId).Count);
    }

    [Theory]
    [InlineData("codex.spark", PlanType.Coding, true)]
    [InlineData("github-copilot", PlanType.Coding, true)]
    public void TryGetUsageSemantics_UsesProviderDefinitions(
        string providerId,
        PlanType expectedPlanType,
        bool expectedIsQuotaBased)
    {
        var def = ProviderMetadataCatalog.Find(providerId);

        Assert.NotNull(def);
        Assert.Equal(expectedPlanType, def.PlanType);
        Assert.Equal(expectedIsQuotaBased, def.IsQuotaBased);
    }

    [Theory]
    [InlineData("codex.spark", "openai")]
    [InlineData("antigravity.claude-opus", "google")]
    [InlineData("unknown-provider", "unknown-provider")]
    public void GetIconAssetName_UsesProviderMetadata(string providerId, string expectedAssetName)
    {
        Assert.Equal(expectedAssetName, ProviderMetadataCatalog.GetIconAssetName(providerId));
    }

    [Theory]
    [InlineData("github-copilot", true, "GH")]
    [InlineData("claude-code", true, "C")]
    [InlineData("unknown-provider", false, "")]
    public void TryGetBadgeDefinition_UsesProviderMetadata(
        string providerId,
        bool expectedSuccess,
        string expectedInitial)
    {
        var success = ProviderMetadataCatalog.TryGetBadgeDefinition(providerId, out _, out var initial);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedInitial, initial);
    }

    [Fact]
    public void GetProviderIdsWithDiscoveryEnvironmentVariables_UsesProviderMetadata()
    {
        var providerIds = ProviderMetadataCatalog.GetProviderIdsWithDiscoveryEnvironmentVariables();

        Assert.Contains("codex", providerIds);
        Assert.Contains("gemini-cli", providerIds);
        Assert.Contains("synthetic", providerIds);
    }

    [Fact]
    public void GetWellKnownProviderIds_UsesProviderMetadata()
    {
        var providerIds = ProviderMetadataCatalog.GetWellKnownProviderIds();

        Assert.Contains("codex", providerIds);
        Assert.Contains("github-copilot", providerIds);
        Assert.DoesNotContain("openai", providerIds);
    }

    [Fact]
    public void GetProviderIdsWithDedicatedSessionAuthFiles_UsesProviderMetadata()
    {
        var providerIds = ProviderMetadataCatalog.GetProviderIdsWithDedicatedSessionAuthFiles();

        Assert.Contains("claude-code", providerIds);
        Assert.Contains("codex", providerIds);
        Assert.DoesNotContain("openai", providerIds);
    }

    [Theory]
    [InlineData("OPENAI_API_KEY", "openai")]
    [InlineData("CODEX_API_KEY", "codex")]
    [InlineData("GEMINI_API_KEY", "gemini-cli")]
    [InlineData("DEEPSEEK_API_KEY", "deepseek")]
    [InlineData("MOONSHOT_API_KEY", "kimi-for-coding")]
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
    [InlineData("deepseek", false)]
    public void ShouldShowInSettings_UsesProviderDefinitions(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.Find(providerId)?.ShowInSettings ?? false);
    }

    [Fact]
    public void GetStartupRefreshProviderIds_UsesProviderDefinitions()
    {
        var providerIds = ProviderMetadataCatalog.GetStartupRefreshProviderIds();

        Assert.Contains("antigravity", providerIds);
        Assert.DoesNotContain("codex", providerIds);
    }

    [Theory]
    [InlineData("github-copilot", PlanType.Coding, true)]
    [InlineData("antigravity", PlanType.Coding, true)]
    [InlineData("gemini-cli", PlanType.Coding, true)]
    [InlineData("kimi-for-coding", PlanType.Coding, true)]
    [InlineData("synthetic", PlanType.Coding, true)]
    [InlineData("zai-coding-plan", PlanType.Coding, true)]
    [InlineData("codex", PlanType.Coding, true)]
    [InlineData("openai", PlanType.Coding, true)]
    public void Definitions_ExposeExplicitQuotaSemanticsForCodingProviders(
        string providerId,
        PlanType expectedPlanType,
        bool expectedIsQuotaBased)
    {
        var definition = Assert.Single(
            ProviderMetadataCatalog.Definitions,
            item => string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expectedPlanType, definition.PlanType);
        Assert.Equal(expectedIsQuotaBased, definition.IsQuotaBased);
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
        Assert.Equal("[Gemini CLI]", gemini.DerivedModelDisplaySuffix);

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
    public void Definitions_ExposeProviderSpecificUiContracts()
    {
        var githubCopilot = Assert.IsType<ProviderDefinition>(ProviderMetadataCatalog.Find("github-copilot"));
        Assert.Equal(ProviderSettingsMode.ExternalAuthStatus, githubCopilot.SettingsMode);
        Assert.Equal(ProviderFamilyMode.Standalone, githubCopilot.FamilyMode);

        var antigravity = Assert.IsType<ProviderDefinition>(ProviderMetadataCatalog.Find("antigravity"));
        Assert.Equal(ProviderSettingsMode.AutoDetectedStatus, antigravity.SettingsMode);
        Assert.Equal(ProviderFamilyMode.DynamicChildProviderRows, antigravity.FamilyMode);
        Assert.True(antigravity.UseChildProviderRowsForGroupedModels);
        Assert.Equal("[Antigravity]", antigravity.DerivedModelDisplaySuffix);

        var gemini = Assert.IsType<ProviderDefinition>(ProviderMetadataCatalog.Find("gemini-cli"));
        Assert.Equal(ProviderSettingsMode.StandardApiKey, gemini.SettingsMode);
        Assert.Equal(ProviderFamilyMode.VisibleDerivedProviders, gemini.FamilyMode);
        Assert.True(gemini.SupportsChildProviderIds);
        Assert.False(gemini.UseChildProviderRowsForGroupedModels);

        foreach (var providerId in new[] { "kimi-for-coding", "synthetic", "zai-coding-plan" })
        {
            var definition = Assert.IsType<ProviderDefinition>(ProviderMetadataCatalog.Find(providerId));
            Assert.Equal(ProviderSettingsMode.StandardApiKey, definition.SettingsMode);
        }
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

    [Theory]
    [InlineData("github-copilot", true)]
    [InlineData("codex", true)]
    [InlineData("openai", true)]
    public void SupportsAccountIdentity_UsesProviderDefinitions(string providerId, bool expected)
    {
        Assert.Equal(expected, ProviderMetadataCatalog.Find(providerId)?.SupportsAccountIdentity ?? false);
    }

    [Fact]
    public void UsageFilter_RemovesNonPersistedProviderIds_UsingPersistenceGate()
    {
        var usages = new List<ProviderUsage>
        {
            new() { ProviderId = "codex", ProviderName = "OpenAI" },
            new() { ProviderId = "openai", ProviderName = "OpenAI" },
        };

        var visible = usages
            .Where(usage => ProviderMetadataCatalog.ShouldPersistProviderId(usage.ProviderId))
            .ToList();

        Assert.Single(visible);
        Assert.Equal("codex", visible[0].ProviderId);
    }

    private static IReadOnlyList<string> GetMissingDerivedModelSelectorProviderIds(string providerId)
    {
        var def = ProviderMetadataCatalog.Find(providerId);
        var visibleDerivedProviderIds = def?.VisibleDerivedProviderIds ?? (IReadOnlyCollection<string>)Array.Empty<string>();
        var selectorProviderIds = (def?.DerivedModelSelectors ?? (IReadOnlyCollection<ProviderDerivedModelSelector>)Array.Empty<ProviderDerivedModelSelector>())
            .Select(selector => selector.DerivedProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return visibleDerivedProviderIds
            .Where(derivedProviderId => !selectorProviderIds.Contains(derivedProviderId))
            .OrderBy(derivedProviderId => derivedProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<string> GetUnknownDerivedModelSelectorProviderIds(string providerId)
    {
        var def = ProviderMetadataCatalog.Find(providerId);
        var visibleDerivedProviderIds = (def?.VisibleDerivedProviderIds ?? (IReadOnlyCollection<string>)Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return (def?.DerivedModelSelectors ?? (IReadOnlyCollection<ProviderDerivedModelSelector>)Array.Empty<ProviderDerivedModelSelector>())
            .Select(selector => selector.DerivedProviderId)
            .Where(derivedProviderId => !visibleDerivedProviderIds.Contains(derivedProviderId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(derivedProviderId => derivedProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
