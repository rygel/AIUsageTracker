// <copyright file="ProviderStatusPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections.ObjectModel;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderStatusPresentationCatalog
{
    public static ProviderStatusPresentation Create(
        ProviderConfig config,
        ProviderUsage? usage,
        ProviderInputMode inputMode,
        bool isPrivacyMode)
    {
        return inputMode switch
        {
            ProviderInputMode.DerivedReadOnly => CreateDerivedPresentation(config, usage),
            ProviderInputMode.AutoDetectedStatus => CreateAutoDetectedPresentation(usage, isPrivacyMode),
            ProviderInputMode.ExternalAuthStatus => CreateExternalAuthPresentation(config, usage, isPrivacyMode),
            ProviderInputMode.SessionAuthStatus => CreateSessionAuthPresentation(config, usage, isPrivacyMode),
            _ => throw new ArgumentOutOfRangeException(
                nameof(inputMode),
                inputMode,
                "Status presentation is only valid for status-based provider modes."),
        };
    }

    public static string MaskAccountIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var atIndex = input.IndexOf('@');
        if (atIndex > 0 && atIndex < input.Length - 1)
        {
            var localPart = input[..atIndex];
            var domainPart = input[(atIndex + 1)..];
            var maskedDomainChars = domainPart.ToCharArray();
            for (var i = 0; i < maskedDomainChars.Length; i++)
            {
                if (maskedDomainChars[i] != '.')
                {
                    maskedDomainChars[i] = '*';
                }
            }

            var maskedDomain = new string(maskedDomainChars);
            if (localPart.Length <= 2)
            {
                return $"{new string('*', localPart.Length)}@{maskedDomain}";
            }

            return $"{localPart[0]}{new string('*', localPart.Length - 2)}{localPart[^1]}@{maskedDomain}";
        }

        return MaskString(input);
    }

    private static ProviderStatusPresentation CreateDerivedPresentation(ProviderConfig config, ProviderUsage? usage)
    {
        var secondaryLines = new List<ProviderStatusLine>();
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(config.ProviderId ?? string.Empty);
        var sourceLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(canonicalProviderId);
        string primaryText;
        string primaryResourceKey;
        if (usage?.IsAvailable == true)
        {
            primaryText = $"Derived from {sourceLabel} usage (read-only)";
            primaryResourceKey = "ProgressBarGreen";
        }
        else if (usage != null && !string.IsNullOrWhiteSpace(usage.Description))
        {
            primaryText = usage.Description;
            primaryResourceKey = "TertiaryText";
        }
        else
        {
            primaryText = "Derived provider (waiting for usage data)";
            primaryResourceKey = "TertiaryText";
        }

        if (usage?.NextResetTime is DateTime derivedReset)
        {
            secondaryLines.Add(new ProviderStatusLine($"Next reset: {derivedReset:g}"));
        }

        return new ProviderStatusPresentation(
            UseHorizontalLayout: false,
            PrimaryText: primaryText,
            PrimaryResourceKey: primaryResourceKey,
            PrimaryItalic: false,
            SecondaryLines: secondaryLines.AsReadOnly());
    }

    private static ProviderStatusPresentation CreateAutoDetectedPresentation(
        ProviderUsage? usage,
        bool isPrivacyMode)
    {
        var isConnected = usage?.IsAvailable == true;
        var accountInfo = usage?.AccountName;
        var hasAccountInfo = !string.IsNullOrWhiteSpace(accountInfo) && accountInfo is not ("Unknown" or "User");
        var displayAccount = hasAccountInfo
            ? (isPrivacyMode ? MaskAccountIdentifier(accountInfo!) : accountInfo!)
            : "No account detected";
        var secondaryLines = new List<ProviderStatusLine>();
        var antigravitySubmodels = usage?.Details?
            .Where(d => d.DetailType == ProviderUsageDetailType.Model && !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => d.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (antigravitySubmodels is { Count: > 0 })
        {
            secondaryLines.Add(new ProviderStatusLine(
                Text: $"Models: {string.Join(", ", antigravitySubmodels)}",
                Wrap: true,
                ExtraTopMargin: true));
        }

        return new ProviderStatusPresentation(
            UseHorizontalLayout: false,
            PrimaryText: isConnected ? $"Auto-Detected ({displayAccount})" : "Searching for local process...",
            PrimaryResourceKey: isConnected ? "ProgressBarGreen" : "TertiaryText",
            PrimaryItalic: !isConnected,
            SecondaryLines: secondaryLines.AsReadOnly());
    }

    private static ProviderStatusPresentation CreateExternalAuthPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isPrivacyMode)
    {
        var username = usage?.AccountName;
        var hasUsername = !string.IsNullOrWhiteSpace(username) && username is not ("Unknown" or "User");
        var isAuthenticated = !string.IsNullOrWhiteSpace(config.ApiKey) ||
                              usage?.IsAvailable == true ||
                              hasUsername;
        var displayText = !isAuthenticated
            ? "Not Authenticated"
            : !hasUsername
                ? "Authenticated"
                : isPrivacyMode
                    ? $"Authenticated ({MaskAccountIdentifier(username!)})"
                    : $"Authenticated ({username})";

        return new ProviderStatusPresentation(
            UseHorizontalLayout: true,
            PrimaryText: displayText,
            PrimaryResourceKey: isAuthenticated ? "ProgressBarGreen" : "TertiaryText",
            PrimaryItalic: false,
            SecondaryLines: Array.Empty<ProviderStatusLine>().ToList().AsReadOnly());
    }

    private static ProviderStatusPresentation CreateSessionAuthPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isPrivacyMode)
    {
        var settingsBehavior = ProviderSettingsCatalog.Resolve(config, usage, isDerived: false);
        var providerSessionLabel = settingsBehavior.SessionProviderLabel ??
                                   ProviderMetadataCatalog.GetConfiguredDisplayName(
                                       ProviderMetadataCatalog.GetCanonicalProviderId(config.ProviderId ?? string.Empty));
        var hasSessionToken = ProviderSettingsCatalog.IsSessionToken(config.ApiKey);
        var isAuthenticated = hasSessionToken || usage?.IsAvailable == true;
        var accountName = usage?.AccountName;

        string displayText;
        if (!isAuthenticated)
        {
            displayText = "Not Authenticated";
        }
        else if (!string.IsNullOrWhiteSpace(accountName))
        {
            displayText = isPrivacyMode
                ? $"Authenticated ({MaskAccountIdentifier(accountName)})"
                : $"Authenticated ({accountName})";
        }
        else if (hasSessionToken && usage?.IsAvailable != true)
        {
            displayText = $"Authenticated via {providerSessionLabel} - refresh to load quota";
        }
        else
        {
            displayText = $"Authenticated via {providerSessionLabel}";
        }

        var secondaryLines = new List<ProviderStatusLine>();
        var resolvedReset = usage?.NextResetTime;
        if (resolvedReset is DateTime nextReset)
        {
            secondaryLines.Add(new ProviderStatusLine($"Next reset: {nextReset:g}"));
        }
        else if (isAuthenticated)
        {
            secondaryLines.Add(new ProviderStatusLine("Next reset: loading..."));
        }

        return new ProviderStatusPresentation(
            UseHorizontalLayout: false,
            PrimaryText: displayText,
            PrimaryResourceKey: isAuthenticated ? "ProgressBarGreen" : "TertiaryText",
            PrimaryItalic: false,
            SecondaryLines: secondaryLines.AsReadOnly());
    }

    private static string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Length <= 2)
        {
            return new string('*', input.Length);
        }

        return input[0] + new string('*', input.Length - 2) + input[^1];
    }
}
