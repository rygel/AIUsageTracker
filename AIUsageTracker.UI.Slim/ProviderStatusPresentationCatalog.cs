using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal sealed record ProviderAuthIdentities(
    string? GitHubUsername,
    string? OpenAiUsername,
    string? CodexUsername);

internal sealed record ProviderStatusLine(
    string Text,
    bool Wrap = false,
    bool ExtraTopMargin = false);

internal sealed record ProviderStatusPresentation(
    bool UseHorizontalLayout,
    string PrimaryText,
    string PrimaryResourceKey,
    bool PrimaryItalic,
    ReadOnlyCollection<ProviderStatusLine> SecondaryLines);

internal static class ProviderStatusPresentationCatalog
{
    public static ProviderStatusPresentation Create(
        ProviderConfig config,
        ProviderUsage? usage,
        ProviderInputMode inputMode,
        bool isPrivacyMode,
        ProviderAuthIdentities authIdentities)
    {
        return inputMode switch
        {
            ProviderInputMode.DerivedReadOnly => CreateDerivedPresentation(usage),
            ProviderInputMode.AntigravityAutoDetected => CreateAntigravityPresentation(usage, isPrivacyMode),
            ProviderInputMode.GitHubCopilotAuthStatus => CreateGitHubPresentation(config, usage, isPrivacyMode, authIdentities),
            ProviderInputMode.OpenAiSessionStatus => CreateOpenAiSessionPresentation(config, usage, isPrivacyMode, authIdentities),
            _ => throw new ArgumentOutOfRangeException(nameof(inputMode), inputMode, "Status presentation is only valid for status-based provider modes.")
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

    private static ProviderStatusPresentation CreateDerivedPresentation(ProviderUsage? usage)
    {
        var secondaryLines = new List<ProviderStatusLine>();
        if (usage?.NextResetTime is DateTime derivedReset)
        {
            secondaryLines.Add(new ProviderStatusLine($"Next reset: {derivedReset:g}"));
        }

        return new ProviderStatusPresentation(
            UseHorizontalLayout: false,
            PrimaryText: usage?.IsAvailable == true
                ? "Derived from Codex usage (read-only)"
                : "Derived provider (waiting for usage data)",
            PrimaryResourceKey: usage?.IsAvailable == true ? "ProgressBarGreen" : "TertiaryText",
            PrimaryItalic: false,
            SecondaryLines: secondaryLines.AsReadOnly());
    }

    private static ProviderStatusPresentation CreateAntigravityPresentation(ProviderUsage? usage, bool isPrivacyMode)
    {
        var isConnected = usage?.IsAvailable == true;
        var accountInfo = usage?.AccountName ?? "Unknown";
        var displayAccount = isPrivacyMode ? MaskAccountIdentifier(accountInfo) : accountInfo;
        var secondaryLines = new List<ProviderStatusLine>();

        var antigravitySubmodels = usage?.Details?
            .Select(d => d.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith("[", StringComparison.Ordinal))
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

    private static ProviderStatusPresentation CreateGitHubPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isPrivacyMode,
        ProviderAuthIdentities authIdentities)
    {
        var username = usage?.AccountName;
        if (string.IsNullOrWhiteSpace(username) || username is "Unknown" or "User")
        {
            username = authIdentities.GitHubUsername;
        }

        var hasUsername = !string.IsNullOrWhiteSpace(username) && username is not ("Unknown" or "User");
        var isAuthenticated = !string.IsNullOrWhiteSpace(config.ApiKey) || !string.IsNullOrWhiteSpace(authIdentities.GitHubUsername);

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

    private static ProviderStatusPresentation CreateOpenAiSessionPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isPrivacyMode,
        ProviderAuthIdentities authIdentities)
    {
        var settingsBehavior = ProviderSettingsCatalog.Resolve(config, usage, isDerived: false);
        var providerSessionLabel = settingsBehavior.SessionProviderLabel ?? "OpenAI";
        var hasSessionToken = ProviderSettingsCatalog.IsSessionToken(config.ApiKey);
        var isAuthenticated = hasSessionToken || usage?.IsAvailable == true;
        var accountName = usage?.AccountName;

        if (string.IsNullOrWhiteSpace(accountName) || accountName is "Unknown" or "User")
        {
            accountName = settingsBehavior.PreferCodexIdentity
                ? (authIdentities.CodexUsername ?? authIdentities.OpenAiUsername)
                : authIdentities.OpenAiUsername;
        }

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
        var resolvedReset = usage?.NextResetTime ?? InferResetTimeFromDetails(usage);
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

    private static DateTime? InferResetTimeFromDetails(ProviderUsage? usage)
    {
        if (usage?.Details == null)
        {
            return null;
        }

        foreach (var detail in usage.Details)
        {
            if (string.IsNullOrWhiteSpace(detail.Description))
            {
                continue;
            }

            var match = Regex.Match(detail.Description, @"Resets in\s+(\d+)s", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds) && seconds > 0)
            {
                return DateTime.Now.AddSeconds(seconds);
            }
        }

        return null;
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
