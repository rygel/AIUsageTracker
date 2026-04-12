// <copyright file="ProviderKeyDeletionEndToEndTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.UI.Slim;

namespace AIUsageTracker.Tests.UI;

/// <summary>
/// End-to-end tests for provider key deletion behavior.
/// Settings always shows all provider cards (they are configuration slots).
/// The monitor snapshot (CachedGroupedUsageProjectionService) excludes unconfigured
/// StandardApiKey providers so they never reach the main window; see
/// CachedGroupedUsageProjectionServiceTests for those assertions.
/// </summary>
public sealed class ProviderKeyDeletionEndToEndTests
{
    // ───────────────────────────────────────────────────────────
    //  Settings: cards are ALWAYS visible (configuration slots)
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Settings_SyntheticCardAlwaysVisible_EvenWithoutConfig()
    {
        var items = SettingsWindow.CreateProviderDisplayItems(
            Array.Empty<ProviderConfig>(), Array.Empty<ProviderUsage>());

        Assert.Contains(items, item =>
            string.Equals(item.Config.ProviderId, "synthetic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Settings_SyntheticCardVisible_AfterKeyRemoval()
    {
        // Simulate: config was removed from _configs after key deletion
        var configs = new List<ProviderConfig>
        {
            new() { ProviderId = "codex", ApiKey = "sk-codex" },
        };

        var items = SettingsWindow.CreateProviderDisplayItems(configs, Array.Empty<ProviderUsage>());

        // Synthetic reappears as a default provider — by design
        Assert.Contains(items, item =>
            string.Equals(item.Config.ProviderId, "synthetic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Settings_EmptyKey_ShowsInactiveBadge()
    {
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = string.Empty };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, null, isDerived: false);

        Assert.Equal(ProviderInputMode.StandardApiKey, behavior.InputMode);
        Assert.True(behavior.IsInactive);
    }

    [Fact]
    public void Settings_WithKey_ShowsActiveBadge()
    {
        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = "sk-test-key" };

        var behavior = SettingsWindow.ResolveProviderSettingsBehavior(config, null, isDerived: false);

        Assert.False(behavior.IsInactive);
    }

    // ───────────────────────────────────────────────────────────
    //  Main window: state does not affect visibility (filtering
    //  of unconfigured providers happens upstream in the monitor)
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void MainWindow_ShowsMissingState_ForSessionAuthProviders()
    {
        // GitHub Copilot uses ExternalAuthStatus — "Not authenticated" is useful info.
        // PrepareForMainWindow imposes no state filter; it renders whatever the monitor snapshot contains.
        var usages = new List<ProviderUsage>
        {
            CreateUsage("github-copilot", isAvailable: false, state: ProviderUsageState.Missing, description: "Not authenticated"),
        };

        var items = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Contains(items, item => item.ProviderId == "github-copilot");
    }

    [Fact]
    public void MainWindow_ShowsErrorState_ForStandardApiKeyProviders()
    {
        // Error state means key exists but API returned an error — still useful
        var usages = new List<ProviderUsage>
        {
            CreateUsage("synthetic", isAvailable: false, state: ProviderUsageState.Error, description: "API Error: 500"),
        };

        var items = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Contains(items, item => item.ProviderId == "synthetic");
    }

    [Fact]
    public void MainWindow_ShowsAvailableState_ForStandardApiKeyProviders()
    {
        var usages = new List<ProviderUsage>
        {
            CreateUsage("synthetic", isAvailable: true, description: "50 / 100 credits"),
        };

        var items = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Contains(items, item => item.ProviderId == "synthetic");
    }

    [Fact]
    public void MainWindow_HiddenProvider_NotRendered()
    {
        var usages = new List<ProviderUsage>
        {
            CreateUsage("synthetic", isAvailable: true, description: "50 / 100"),
        };

        var items = MainWindowRuntimeLogic.PrepareForMainWindow(usages, new[] { "synthetic" });

        Assert.DoesNotContain(items, item => item.ProviderId == "synthetic");
    }

    [Fact]
    public void MainWindow_NoUsageData_NoCard()
    {
        var usages = new List<ProviderUsage>
        {
            CreateUsage("codex", isAvailable: true),
        };

        var items = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.DoesNotContain(items, item => item.ProviderId == "synthetic");
    }

    // ───────────────────────────────────────────────────────────
    //  Provider metadata assertions
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void Synthetic_IsDefaultSettingsProvider()
    {
        Assert.Contains("synthetic", ProviderMetadataCatalog.GetDefaultSettingsProviderIds(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Synthetic_UsesStandardApiKeyMode()
    {
        var definition = ProviderMetadataCatalog.Find("synthetic");

        Assert.NotNull(definition);
        Assert.Equal(ProviderSettingsMode.StandardApiKey, definition.SettingsMode);
    }

    [Fact]
    public void Synthetic_ShowInMainWindow()
    {
        var definition = ProviderMetadataCatalog.Find("synthetic");

        Assert.NotNull(definition);
        Assert.True(definition.ShowInMainWindow);
    }

    [Fact]
    public void Synthetic_IsNotVisibleDerivedProviderId()
    {
        Assert.False(ProviderMetadataCatalog.IsVisibleDerivedProviderId("synthetic"));
    }

    // ───────────────────────────────────────────────────────────
    //  Expired state: subscription lapsed but key still present
    // ───────────────────────────────────────────────────────────

    [Fact]
    public void MainWindow_ShowsExpiredState_ForStandardApiKeyProviders()
    {
        // Expired means key exists but subscription lapsed — card must stay visible
        var usages = new List<ProviderUsage>
        {
            CreateUsage("synthetic", isAvailable: false, state: ProviderUsageState.Expired, description: "No active subscription"),
        };

        var items = MainWindowRuntimeLogic.PrepareForMainWindow(usages);

        Assert.Contains(items, item => item.ProviderId == "synthetic");
    }

    [Fact]
    public void MainWindow_ExpiredCard_RendersWarningTone()
    {
        var usage = CreateUsage("synthetic", isAvailable: false, state: ProviderUsageState.Expired, description: "No active subscription");

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed: false);

        Assert.True(presentation.IsExpired);
        Assert.Equal(ProviderCardStatusTone.Warning, presentation.StatusTone);
        Assert.Equal("No active subscription", presentation.StatusText);
        Assert.False(presentation.ShouldHaveProgress);
    }

    [Fact]
    public void MainWindow_ShowsExpired_WhenSubscriptionLapsed()
    {
        // Expired = key present but subscription lapsed → card must remain visible so the
        // user can see their quota is exhausted. Filtering of Missing (no key) happens
        // upstream in CachedGroupedUsageProjectionService, not here.
        var expiredUsages = new List<ProviderUsage>
        {
            CreateUsage("synthetic", isAvailable: false, state: ProviderUsageState.Expired, description: "No active subscription"),
        };

        var expiredItems = MainWindowRuntimeLogic.PrepareForMainWindow(expiredUsages);

        Assert.Contains(expiredItems, item => item.ProviderId == "synthetic");
    }

    // ───────────────────────────────────────────────────────────
    //  External auth source warning on key removal
    // ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Env: SYNTHETIC_API_KEY", true)]
    [InlineData("Roo Code: C:\\Users\\user\\.roo\\secrets.json", true)]
    [InlineData("Kilo Code Roo Config", true)]
    [InlineData("Config: configs.json", false)]
    [InlineData("", false)]
    public void ExternalAuthSourceDetection_IdentifiesExternalSources(string authSource, bool shouldWarn)
    {
        var isExternal = AuthSource.IsRooOrKilo(authSource) || AuthSource.IsEnvironment(authSource);

        Assert.Equal(shouldWarn, isExternal);
    }

    // ───────────────────────────────────────────────────────────
    //  Provider behavior with empty key
    // ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SyntheticProvider_NoKey_ReturnsMissingState()
    {
        var httpClient = new HttpClient(new NoopHandler());
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SyntheticProvider>.Instance;
        var provider = new SyntheticProvider(httpClient, logger);

        var config = new ProviderConfig { ProviderId = "synthetic", ApiKey = string.Empty };
        var result = (await provider.GetUsageAsync(config)).ToList();

        Assert.Single(result);
        Assert.Equal(ProviderUsageState.Missing, result[0].State);
        Assert.False(result[0].IsAvailable);
    }

    // ───────────────────────────────────────────────────────────
    //  Helpers
    // ───────────────────────────────────────────────────────────

    private static ProviderUsage CreateUsage(
        string providerId,
        bool isAvailable = true,
        ProviderUsageState state = ProviderUsageState.Available,
        string description = "")
    {
        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = providerId,
            IsAvailable = isAvailable,
            State = state,
            Description = description,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
        };
    }

    private sealed class NoopHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Should not be called when key is empty");
        }
    }
}
