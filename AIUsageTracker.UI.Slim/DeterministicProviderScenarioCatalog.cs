// <copyright file="DeterministicProviderScenarioCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class DeterministicProviderScenarioCatalog
{
    public static IReadOnlyList<DeterministicProviderScenario> Scenarios { get; } =
    [
        new(
            ClaudeCodeProvider.StaticDefinition.ProviderId,
            "cc-demo-key",
            ShowInTray: true,
            AuthSource: "local credentials",
            MainWindowUsage: new(),
            SettingsWindowUsage: new()),
        new(
            DeepSeekProvider.StaticDefinition.ProviderId,
            "sk-ds-demo",
            SettingsWindowUsage: new()),
        new(
            GeminiProvider.StaticDefinition.ProviderId,
            "gemini-local-auth",
            AuthSource: "local auth",
            SettingsWindowUsage: new(16.0, 0, 0, "84.0% Remaining", 12)),
        new(
            GitHubCopilotProvider.StaticDefinition.ProviderId,
            "ghp_demo_key",
            ShowInTray: true,
            EnableNotifications: true,
            AuthSource: "oauth",
            MainWindowUsage: new(27.5, 110, 400, "72.5% Remaining", 20),
            SettingsWindowUsage: new(27.5, 0, 0, "72.5% Remaining", 20),
            SettingsWindowHistory: new(27.5, 27.5, 100.0, "72.5% Remaining", new DateTime(2026, 2, 1, 12, 0, 0))),
        new(
            KimiProvider.StaticDefinition.ProviderId,
            "kimi-demo-key",
            SettingsWindowUsage: new(34.0, 0, 0, "66.0% Remaining", 9)),
        new(
            MinimaxProvider.StaticDefinition.ProviderId,
            "mm-cn-demo",
            SettingsWindowUsage: new(39.0, 0, 0, "61.0% Remaining", 11)),
        new(
            MinimaxProvider.InternationalProviderId,
            "mm-intl-demo",
            SettingsWindowUsage: new()),
        new(
            MistralProvider.StaticDefinition.ProviderId,
            "mistral-demo-key",
            MainWindowUsage: new(Description: "Connected"),
            SettingsWindowUsage: new()),
        new(
            OpenAIProvider.StaticDefinition.ProviderId,
            "sk-openai-demo",
            ShowInTray: true,
            SettingsWindowUsage: new(37.0, 0, 0, "63.0% Remaining", 18),
            SettingsWindowHistory: new(31.1, 12.45, 40.0, "$12.45 / $40.00", new DateTime(2026, 2, 1, 12, 5, 0))),
        new(
            OpenCodeZenProvider.StaticDefinition.ProviderId,
            "ocz-demo-key",
            SettingsWindowUsage: new()),
        new(
            OpenRouterProvider.StaticDefinition.ProviderId,
            "or-demo-key",
            SettingsWindowUsage: new()),
        new(
            SyntheticProvider.StaticDefinition.ProviderId,
            "syn-demo-key",
            MainWindowUsage: new(9.0, 18, 200, "91.0% Remaining", 4),
            SettingsWindowUsage: new(21.0, 0, 0, "79.0% Remaining", 4)),
        new(
            ZaiProvider.StaticDefinition.ProviderId,
            "zai-demo-key",
            ShowInTray: true,
            MainWindowUsage: new(18.0, 45, 250, "82.0% Remaining", 12),
            SettingsWindowUsage: new(12.0, 0, 0, "88.0% Remaining", 15)),
    ];
}
