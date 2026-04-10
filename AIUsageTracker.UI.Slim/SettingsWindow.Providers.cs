// <copyright file="SettingsWindow.Providers.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class SettingsWindow
{
    private sealed record StatusPanelPresentation(
        bool UseHorizontalLayout,
        string PrimaryText,
        string PrimaryResourceKey,
        bool PrimaryItalic,
        IReadOnlyList<StatusSecondaryLine> SecondaryLines);

    private readonly record struct StatusSecondaryLine(
        string Text,
        bool Wrap = false,
        bool ExtraTopMargin = false);

    private void PopulateProviders()
    {
        this.ProvidersStack.Children.Clear();

        var displayItems = CreateProviderDisplayItems(this._configs, this._usages);
        foreach (var item in displayItems)
        {
            var usage = this._usages.FirstOrDefault(u =>
                string.Equals(u.ProviderId, item.Config.ProviderId, StringComparison.OrdinalIgnoreCase));
            this.AddProviderCard(item.Config, usage, item.IsDerived);
        }

        this.PopulateProviderVisibilitySettings();
    }

    private void AddProviderCard(ProviderConfig config, ProviderUsage? usage, bool isDerived = false)
    {
        var isSubItem = ShouldRenderAsSettingsSubItem(config.ProviderId, isDerived);

        var card = new Border
        {
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(isSubItem ? 18 : 0, 0, 0, 8),
            Padding = new Thickness(10, 8, 10, 8),
        };
        card.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        card.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Inputs

        var settingsBehavior = ResolveProviderSettingsBehavior(config, usage, isDerived);
        var headerPanel = this.BuildProviderHeader(config, settingsBehavior, isSubItem);

        grid.Children.Add(headerPanel);

        // Input row
        var keyPanel = new Grid { Margin = new Thickness(0, 0, 0, 0) };
        keyPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyContent = this.BuildProviderInputContent(config, usage, settingsBehavior);
        Grid.SetColumn(keyContent, 0);
        keyPanel.Children.Add(keyContent);

        Grid.SetRow(keyPanel, 1);
        grid.Children.Add(keyPanel);

        card.Child = grid;
        this.ProvidersStack.Children.Add(card);
    }

    internal static bool ShouldRenderAsSettingsSubItem(
        string providerId,
        bool isDerived) => false;

    internal static IReadOnlyList<string> GetEligibleSubTrayDetails(ProviderUsage? usage)
    {
        // Sub-tray details are no longer derived from ProviderUsageDetail.
        // Flat ProviderUsage cards replaced the detail model; sub-tray icons are not supported.
        return Array.Empty<string>();
    }

    internal static IReadOnlyList<ProviderSettingsDisplayItem> CreateProviderDisplayItems(
        IReadOnlyCollection<ProviderConfig> configs,
        IReadOnlyCollection<ProviderUsage> usages)
    {
        var displayItems = configs
            .Where(config => ProviderMetadataCatalog.Find(config.ProviderId)?.ShowInSettings ?? false)
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false))
            .ToList();
        var configuredProviderIds = displayItems
            .Select(item => item.Config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultProviderIds = ProviderMetadataCatalog.GetDefaultSettingsProviderIds()
            .Where(providerId => !configuredProviderIds.Contains(providerId))
            .ToList();

        var defaultItems = defaultProviderIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(CreateDefaultDisplayConfig)
            .Select(config => new ProviderSettingsDisplayItem(config, IsDerived: false));
        var explicitDisplayProviderIds = configuredProviderIds
            .Concat(defaultProviderIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var derivedItems = usages
            .Select(usage => new { Usage = usage, ProviderId = usage.ProviderId ?? string.Empty })
            .Where(x =>
                !string.IsNullOrWhiteSpace(x.ProviderId) &&
                ProviderMetadataCatalog.IsVisibleDerivedProviderId(x.ProviderId) &&
                !explicitDisplayProviderIds.Contains(x.ProviderId))
            .Select(x => x.Usage)
            .Select(usage => new ProviderSettingsDisplayItem(CreateDerivedConfig(usage), IsDerived: true));

        displayItems.AddRange(defaultItems);
        displayItems.AddRange(derivedItems);

        return displayItems
            .OrderBy(item => ProviderMetadataCatalog.ResolveDisplayLabel(item.Config.ProviderId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Config.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static ProviderSettingsBehavior ResolveProviderSettingsBehavior(
        ProviderConfig config,
        ProviderUsage? usage,
        bool isDerived)
    {
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(config.ProviderId);
        var hasSessionToken = IsSessionToken(config.ApiKey);
        var inputMode = isDerived
            ? ProviderInputMode.DerivedReadOnly
            : ResolveProviderInputMode(canonicalProviderId, usage, hasSessionToken);
        var isInactive = isDerived
            ? false
            : inputMode switch
            {
                ProviderInputMode.AutoDetectedStatus => usage == null || !usage.IsAvailable,
                ProviderInputMode.SessionAuthStatus => string.IsNullOrWhiteSpace(config.ApiKey) && !(usage?.IsAvailable == true),
                _ => string.IsNullOrWhiteSpace(config.ApiKey),
            };
        var sessionProviderLabel = inputMode == ProviderInputMode.SessionAuthStatus
            ? ProviderMetadataCatalog.Find(canonicalProviderId)?.SessionStatusLabel
            : null;

        return new ProviderSettingsBehavior(
            InputMode: inputMode,
            IsInactive: isInactive,
            IsDerivedVisible: ProviderMetadataCatalog.IsVisibleDerivedProviderId(config.ProviderId ?? string.Empty),
            SessionProviderLabel: sessionProviderLabel);
    }

    internal static bool IsSessionToken(string? apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) &&
               !apiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderConfig CreateDefaultDisplayConfig(string providerId)
    {
        if (ProviderMetadataCatalog.TryCreateDefaultConfig(providerId, out var config))
        {
            return config;
        }

        return new ProviderConfig
        {
            ProviderId = providerId,
        };
    }

    private static ProviderConfig CreateDerivedConfig(ProviderUsage usage)
    {
        return new ProviderConfig
        {
            ProviderId = usage.ProviderId,
        };
    }

    private static ProviderInputMode ResolveProviderInputMode(string canonicalProviderId, ProviderUsage? usage, bool hasSessionToken)
    {
        var settingsDef = ProviderMetadataCatalog.Find(canonicalProviderId);
        var settingsMode = settingsDef?.SettingsMode ?? ProviderSettingsMode.StandardApiKey;
        if (settingsMode == ProviderSettingsMode.SessionAuthStatus &&
            (settingsDef?.UseSessionAuthStatusWhenQuotaBasedOrSessionToken ?? false) &&
            usage?.IsQuotaBased != true &&
            !hasSessionToken)
        {
            settingsMode = ProviderSettingsMode.StandardApiKey;
        }

        return settingsMode switch
        {
            ProviderSettingsMode.AutoDetectedStatus => ProviderInputMode.AutoDetectedStatus,
            ProviderSettingsMode.ExternalAuthStatus => ProviderInputMode.ExternalAuthStatus,
            ProviderSettingsMode.SessionAuthStatus => ProviderInputMode.SessionAuthStatus,
            _ => ProviderInputMode.StandardApiKey,
        };
    }

    private FrameworkElement BuildProviderInputContent(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        return settingsBehavior.InputMode switch
        {
            ProviderInputMode.DerivedReadOnly
                or ProviderInputMode.AutoDetectedStatus
                or ProviderInputMode.ExternalAuthStatus
                or ProviderInputMode.SessionAuthStatus
                => this.BuildStatusPanel(config, usage, settingsBehavior),
            _ => this.BuildApiKeyEditor(config),
        };
    }

    private StackPanel BuildStatusPanel(ProviderConfig config, ProviderUsage? usage, ProviderSettingsBehavior settingsBehavior)
    {
        var presentation = this.CreateStatusPresentation(
            config,
            usage,
            settingsBehavior,
            this._isPrivacyMode);

        var panel = new StackPanel
        {
            Orientation = presentation.UseHorizontalLayout
                ? Orientation.Horizontal
                : Orientation.Vertical,
        };

        var statusText = new TextBlock
        {
            Text = presentation.PrimaryText,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontStyle = presentation.PrimaryItalic ? FontStyles.Italic : FontStyles.Normal,
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, presentation.PrimaryResourceKey);
        panel.Children.Add(statusText);

        foreach (var line in presentation.SecondaryLines)
        {
            var secondaryText = this.CreateSecondaryStatusText(line.Text);
            secondaryText.TextWrapping = line.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            if (line.ExtraTopMargin)
            {
                secondaryText.Margin = new Thickness(0, 4, 0, 0);
            }

            panel.Children.Add(secondaryText);
        }

        return panel;
    }

    private StatusPanelPresentation CreateStatusPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        ProviderSettingsBehavior settingsBehavior,
        bool isPrivacyMode)
    {
        return settingsBehavior.InputMode switch
        {
            ProviderInputMode.DerivedReadOnly => this.CreateDerivedStatusPresentation(config, usage),
            ProviderInputMode.AutoDetectedStatus => CreateAutoDetectedStatusPresentation(usage, isPrivacyMode),
            ProviderInputMode.ExternalAuthStatus => CreateExternalAuthStatusPresentation(config, usage, isPrivacyMode),
            ProviderInputMode.SessionAuthStatus => CreateSessionAuthStatusPresentation(config, usage, settingsBehavior, isPrivacyMode),
            _ => throw new ArgumentOutOfRangeException(
                nameof(settingsBehavior),
                settingsBehavior.InputMode,
                "Status presentation is only valid for status-based provider modes."),
        };
    }

    private StatusPanelPresentation CreateDerivedStatusPresentation(
        ProviderConfig config,
        ProviderUsage? usage)
    {
        var secondaryLines = new List<StatusSecondaryLine>();
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
            secondaryLines.Add(new StatusSecondaryLine($"Next reset: {derivedReset:g}"));
        }

        return new StatusPanelPresentation(
            UseHorizontalLayout: false,
            PrimaryText: primaryText,
            PrimaryResourceKey: primaryResourceKey,
            PrimaryItalic: false,
            SecondaryLines: secondaryLines);
    }

    private static StatusPanelPresentation CreateAutoDetectedStatusPresentation(
        ProviderUsage? usage,
        bool isPrivacyMode)
    {
        var isConnected = usage?.IsAvailable == true;
        var accountInfo = usage?.AccountName;
        var hasAccountInfo = !string.IsNullOrWhiteSpace(accountInfo) && accountInfo is not ("Unknown" or "User");
        var displayAccount = hasAccountInfo
            ? (isPrivacyMode ? PrivacyHelper.MaskAccountIdentifier(accountInfo!) : accountInfo!)
            : "No account detected";
        var secondaryLines = new List<StatusSecondaryLine>();

        return new StatusPanelPresentation(
            UseHorizontalLayout: false,
            PrimaryText: isConnected ? $"Auto-Detected ({displayAccount})" : "Searching for local process...",
            PrimaryResourceKey: isConnected ? "ProgressBarGreen" : "TertiaryText",
            PrimaryItalic: !isConnected,
            SecondaryLines: secondaryLines);
    }

    private static StatusPanelPresentation CreateExternalAuthStatusPresentation(
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
                    ? $"Authenticated ({PrivacyHelper.MaskAccountIdentifier(username!)})"
                    : $"Authenticated ({username})";

        return new StatusPanelPresentation(
            UseHorizontalLayout: true,
            PrimaryText: displayText,
            PrimaryResourceKey: isAuthenticated ? "ProgressBarGreen" : "TertiaryText",
            PrimaryItalic: false,
            SecondaryLines: Array.Empty<StatusSecondaryLine>());
    }

    private static StatusPanelPresentation CreateSessionAuthStatusPresentation(
        ProviderConfig config,
        ProviderUsage? usage,
        ProviderSettingsBehavior settingsBehavior,
        bool isPrivacyMode)
    {
        var providerSessionLabel = settingsBehavior.SessionProviderLabel ??
                                   ProviderMetadataCatalog.GetConfiguredDisplayName(
                                       ProviderMetadataCatalog.GetCanonicalProviderId(config.ProviderId ?? string.Empty));
        var hasSessionToken = IsSessionToken(config.ApiKey);
        var isAuthenticated = hasSessionToken || usage?.IsAvailable == true;
        var accountName = usage?.AccountName;

        var displayText = ResolveSessionAuthDisplayText(
            isAuthenticated,
            hasSessionToken,
            accountName,
            providerSessionLabel,
            usage?.IsAvailable,
            isPrivacyMode);

        var secondaryLines = new List<StatusSecondaryLine>();
        var resolvedReset = usage?.NextResetTime;
        if (resolvedReset is DateTime nextReset)
        {
            secondaryLines.Add(new StatusSecondaryLine($"Next reset: {nextReset:g}"));
        }
        else if (isAuthenticated)
        {
            secondaryLines.Add(new StatusSecondaryLine("Next reset: loading..."));
        }

        return new StatusPanelPresentation(
            UseHorizontalLayout: false,
            PrimaryText: displayText,
            PrimaryResourceKey: isAuthenticated ? "ProgressBarGreen" : "TertiaryText",
            PrimaryItalic: false,
            SecondaryLines: secondaryLines);
    }

    private static string ResolveSessionAuthDisplayText(
        bool isAuthenticated,
        bool hasSessionToken,
        string? accountName,
        string providerSessionLabel,
        bool? isUsageAvailable,
        bool isPrivacyMode)
    {
        if (!isAuthenticated)
        {
            return "Not Authenticated";
        }

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            return isPrivacyMode
                ? $"Authenticated ({PrivacyHelper.MaskAccountIdentifier(accountName)})"
                : $"Authenticated ({accountName})";
        }

        return hasSessionToken && isUsageAvailable != true
            ? $"Authenticated via {providerSessionLabel} - refresh to load quota"
            : $"Authenticated via {providerSessionLabel}";
    }

    private StackPanel BuildProviderHeader(ProviderConfig config, ProviderSettingsBehavior settingsBehavior, bool isDerived)
    {
        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        var icon = this.CreateProviderIcon(config.ProviderId);
        icon.Width = 16;
        icon.Height = 16;
        icon.Margin = new Thickness(0, 0, 8, 0);
        icon.VerticalAlignment = VerticalAlignment.Center;
        headerPanel.Children.Add(icon);

        var title = new TextBlock
        {
            Text = isDerived
                ? $"-> {ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId)}"
                : ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId),
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
        headerPanel.Children.Add(title);

        headerPanel.Children.Add(this.CreateProviderHeaderCheckBox(
            content: "Tray",
            isChecked: config.ShowInTray,
            margin: new Thickness(12, 0, 0, 0),
            isEnabled: !isDerived,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.ShowInTray = isChecked;
                this.MarkSettingsChanged(refreshTrayIcons: true);
            }));

        headerPanel.Children.Add(this.CreateProviderHeaderCheckBox(
            content: "Notify",
            isChecked: config.EnableNotifications,
            margin: new Thickness(8, 0, 0, 0),
            isEnabled: !isDerived,
            onCheckedChanged: isChecked =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.EnableNotifications = isChecked;
                this.MarkSettingsChanged();
            }));

        var definition = ProviderMetadataCatalog.Find(config.ProviderId);
        if (!isDerived &&
            settingsBehavior.InputMode == ProviderInputMode.AutoDetectedStatus &&
            definition?.FamilyMode == ProviderFamilyMode.FlatWindowCards)
        {
            headerPanel.Children.Add(this.CreateProviderHeaderCheckBox(
                content: "Models offline",
                isChecked: config.ShowCachedModelsWhenOffline,
                margin: new Thickness(8, 0, 0, 0),
                isEnabled: true,
                onCheckedChanged: isChecked =>
                {
                    var trackedConfig = this.GetOrCreateTrackedConfig(config);
                    trackedConfig.ShowCachedModelsWhenOffline = isChecked;
                    this.MarkSettingsChanged();
                }));
        }

        if (settingsBehavior.IsInactive)
        {
            headerPanel.Children.Add(this.CreateInactiveBadge());
        }

        return headerPanel;
    }

    private CheckBox CreateProviderHeaderCheckBox(
        string content,
        bool isChecked,
        Thickness margin,
        bool isEnabled,
        Action<bool> onCheckedChanged)
    {
        var checkBox = new CheckBox
        {
            Content = content,
            IsChecked = isChecked,
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Margin = margin,
            IsEnabled = isEnabled,
        };
        checkBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        checkBox.Checked += (_, _) => onCheckedChanged(true);
        checkBox.Unchecked += (_, _) => onCheckedChanged(false);
        return checkBox;
    }

    private Border CreateInactiveBadge()
    {
        var status = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(205, 92, 92)),
            CornerRadius = new CornerRadius(3),
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(8, 3, 8, 3),
        };

        status.Child = new TextBlock
        {
            Text = "Inactive",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
            FontWeight = FontWeights.SemiBold,
        };
        return status;
    }

    private CheckBox CreateSubTrayCheckBox(ProviderConfig config, string detailName)
    {
        var enabledSubTrays = config.EnabledSubTrays ?? new List<string>();
        var checkBox = new CheckBox
        {
            Content = detailName,
            IsChecked = enabledSubTrays.Contains(detailName, StringComparer.OrdinalIgnoreCase),
            FontSize = 10,
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
        };
        checkBox.SetResourceReference(CheckBox.ForegroundProperty, "SecondaryText");
        checkBox.Checked += (_, _) =>
        {
            var trackedConfig = this.GetOrCreateTrackedConfig(config);
            trackedConfig.EnabledSubTrays ??= new List<string>();
            if (!trackedConfig.EnabledSubTrays.Contains(detailName, StringComparer.OrdinalIgnoreCase))
            {
                var enabledSubTrays = trackedConfig.EnabledSubTrays.ToList();
                enabledSubTrays.Add(detailName);
                trackedConfig.EnabledSubTrays = enabledSubTrays;
            }

            this.MarkSettingsChanged(refreshTrayIcons: true);
        };
        checkBox.Unchecked += (_, _) =>
        {
            var trackedConfig = this.GetOrCreateTrackedConfig(config);
            trackedConfig.EnabledSubTrays ??= new List<string>();
            var enabledSubTrays = trackedConfig.EnabledSubTrays.ToList();
            enabledSubTrays.RemoveAll(name => name.Equals(detailName, StringComparison.OrdinalIgnoreCase));
            trackedConfig.EnabledSubTrays = enabledSubTrays;
            this.MarkSettingsChanged(refreshTrayIcons: true);
        };
        return checkBox;
    }

    private FrameworkElement BuildApiKeyEditor(ProviderConfig config)
    {
        var keyBox = new TextBox
        {
            Text = GetDisplayApiKey(config.ApiKey, this._isPrivacyMode),
            Tag = config,
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            IsReadOnly = this._isPrivacyMode,
        };

        if (!this._isPrivacyMode)
        {
            keyBox.TextChanged += (s, e) =>
            {
                var trackedConfig = this.GetOrCreateTrackedConfig(config);
                trackedConfig.ApiKey = keyBox.Text;
                this.MarkSettingsChanged();
            };
        }

        var authSourcePanel = BuildAuthSourcePanel(config.AuthSource);
        if (authSourcePanel == null)
        {
            return keyBox;
        }

        var panel = new StackPanel();
        panel.Children.Add(keyBox);
        panel.Children.Add(authSourcePanel);
        return panel;
    }

    private static FrameworkElement? BuildAuthSourcePanel(string? authSource)
    {
        var (sourceLabel, removalHint, paths) = ResolveAuthSourceDisplay(authSource);
        if (sourceLabel == null)
        {
            return null;
        }

        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        // Source line
        var sourceLine = new TextBlock
        {
            FontSize = 9,
            Margin = new Thickness(0, 0, 0, 1),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        sourceLine.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        sourceLine.Inlines.Add(new System.Windows.Documents.Run("Source: ") { FontWeight = FontWeights.SemiBold });
        sourceLine.Inlines.Add(new System.Windows.Documents.Run(sourceLabel));
        if (!string.IsNullOrEmpty(removalHint))
        {
            sourceLine.ToolTip = $"To remove: {removalHint}";
        }

        panel.Children.Add(sourceLine);

        // File path lines (one per path, selectable for copy + edit/folder buttons)
        foreach (var path in paths)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Text = path,
                FontSize = 8,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0),
                TextWrapping = TextWrapping.NoWrap,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                ToolTip = path,
                VerticalAlignment = VerticalAlignment.Center,
            };
            pathBox.SetResourceReference(TextBox.ForegroundProperty, "SecondaryText");
            Grid.SetColumn(pathBox, 0);
            row.Children.Add(pathBox);

            var capturedPath = path;
            var fileExists = File.Exists(path);

            var editButton = new Button
            {
                Content = "Edit",
                FontSize = 9,
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = fileExists ? $"Open in Notepad: {path}" : "File does not exist yet",
                IsEnabled = fileExists,
            };
            editButton.SetResourceReference(Button.BackgroundProperty, "AccentColor");
            editButton.SetResourceReference(Button.ForegroundProperty, "AccentForeground");
            editButton.Click += (_, _) => OpenInNotepad(capturedPath);
            Grid.SetColumn(editButton, 1);
            row.Children.Add(editButton);

            var folderButton = new Button
            {
                Content = "Folder",
                FontSize = 9,
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = $"Show in Explorer: {Path.GetDirectoryName(path)}",
            };
            folderButton.Click += (_, _) => OpenPathInExplorer(capturedPath);
            Grid.SetColumn(folderButton, 2);
            row.Children.Add(folderButton);

            panel.Children.Add(row);
        }

        return panel;
    }

    private static void OpenInNotepad(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static void OpenPathInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                });
            }
            else
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{dir}\"",
                        UseShellExecute = true,
                    });
                }
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // If shell open fails, silently ignore — the path is still selectable for manual navigation.
        }
    }

    private static (string? SourceLabel, string? RemovalHint, IReadOnlyList<string> Paths) ResolveAuthSourceDisplay(string? authSource)
    {
        if (string.IsNullOrWhiteSpace(authSource) ||
            string.Equals(authSource, AuthSource.None, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null, Array.Empty<string>());
        }

        // Environment variable
        if (AuthSource.TryParseEnvironmentVariable(authSource, out var varName))
        {
            return (
                $"Environment variable {varName}",
                $"Delete the {varName} environment variable from System Properties → Environment Variables, then restart.",
                Array.Empty<string>());
        }

        // Roo Code
        if (AuthSource.TryParseRooPath(authSource, out var rooPath))
        {
            return (
                "Roo Code",
                "Edit or delete the file below to remove the key from Roo Code.",
                new[] { rooPath });
        }

        // Kilo Code
        if (AuthSource.IsRooOrKilo(authSource))
        {
            return (
                "Kilo Code",
                "Remove the key from Kilo Code settings.",
                Array.Empty<string>());
        }

        // Config file(s) — show full paths
        var configPaths = AuthSource.ParseConfigFilePaths(authSource);
        if (configPaths.Count > 0)
        {
            // Determine human-readable source application from paths
            var appName = ResolveConfigSourceAppName(configPaths);
            return (
                appName,
                "Edit or delete the file(s) below, or clear the key field above and save.",
                configPaths);
        }

        // Fallback for other known constants (OpenCode Session, Codex Native, etc.)
        return (authSource, null, Array.Empty<string>());
    }

    private static string ResolveConfigSourceAppName(IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            if (path.Contains("opencode", StringComparison.OrdinalIgnoreCase))
            {
                return "OpenCode";
            }

            if (path.Contains("roo", StringComparison.OrdinalIgnoreCase))
            {
                return "Roo Code";
            }

            if (path.Contains("kilo", StringComparison.OrdinalIgnoreCase))
            {
                return "Kilo Code";
            }

            if (path.Contains("AIUsageTracker", StringComparison.OrdinalIgnoreCase))
            {
                return "AI Usage Tracker";
            }
        }

        return "Config file";
    }

    private static string GetDisplayApiKey(string? apiKey, bool isPrivacyMode)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            return apiKey ?? string.Empty;
        }

        if (!isPrivacyMode)
        {
            return apiKey;
        }

        if (apiKey.Length > 8)
        {
            return apiKey[..4] + "****" + apiKey[^4..];
        }

        return "****";
    }

    private TextBlock CreateSecondaryStatusText(string text)
    {
        var statusText = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 10,
            Margin = new Thickness(0, 3, 0, 0),
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryText");
        return statusText;
    }

    private FrameworkElement CreateProviderIcon(string providerId)
    {
        // Map to SVG or create fallback
        var image = new Image();
        image.Source = this.GetProviderImageSource(providerId);
        return image;
    }

    private ImageSource GetProviderImageSource(string providerId)
    {
        try
        {
            var filename = ProviderMetadataCatalog.GetIconAssetName(providerId);

            var appDir = AppDomain.CurrentDomain.BaseDirectory;

            // Try SVG first
            var svgPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.svg");
            if (System.IO.File.Exists(svgPath))
            {
                // Return a simple colored circle as fallback (SVG loading requires SharpVectors)
                return this.CreateFallbackIcon(providerId);
            }

            // Try ICO
            var icoPath = System.IO.Path.Combine(appDir, "Assets", "ProviderLogos", $"{filename}.ico");
            if (System.IO.File.Exists(icoPath))
            {
                var icoImage = new System.Windows.Media.Imaging.BitmapImage();
                icoImage.BeginInit();
                icoImage.UriSource = new Uri(icoPath);
                icoImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                icoImage.EndInit();
                icoImage.Freeze();
                return icoImage;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or NotSupportedException)
        {
            this._logger.LogDebug(ex, "Failed to load provider icon for {ProviderId}", providerId);
        }

        return this.CreateFallbackIcon(providerId);
    }

    private ImageSource CreateFallbackIcon(string providerId)
    {
        // Create a simple colored circle as fallback
        var (color, _) = global::AIUsageTracker.UI.Slim.Services.WpfProviderIconService.GetBadge(providerId, Brushes.Gray);

        // Return a drawing image with just a colored rectangle (simplified)
        var drawing = new GeometryDrawing(
            color,
            new Pen(Brushes.Transparent, 0),
            new RectangleGeometry(new Rect(0, 0, 16, 16)));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }

    private ProviderConfig GetOrCreateTrackedConfig(ProviderConfig config)
    {
        var existing = this._configs.FirstOrDefault(current =>
            current.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        var tracked = this.CloneConfig(config);
        this._configs.Add(tracked);
        return tracked;
    }

    private ProviderConfig CloneConfig(ProviderConfig config)
    {
        return new ProviderConfig
        {
            ProviderId = config.ProviderId,
            ApiKey = config.ApiKey,
            Limit = config.Limit,
            BaseUrl = config.BaseUrl,
            ShowInTray = config.ShowInTray,
            EnableNotifications = config.EnableNotifications,
            EnabledSubTrays = config.EnabledSubTrays.ToList(),
            AuthSource = config.AuthSource,
            Description = config.Description,
            Models = config.Models
                .Select(model => new AIModelConfig
                {
                    Id = model.Id,
                    Name = model.Name,
                    Matches = model.Matches.ToList(),
                    Color = model.Color,
                })
                .ToList(),
        };
    }

    private void PopulateProviderVisibilitySettings()
    {
        this.ProviderCardVisibilityPanel.Children.Clear();
        var hidden = this._preferences.HiddenProviderItemIds;

        // Run the same pipeline as the main window (no hidden filter) to get every card
        // that could potentially appear, then group by canonical provider.
        var allCards = MainWindowRuntimeLogic.BuildMainWindowUsageList(this._usages).ToList();

        var groups = allCards
            .GroupBy(
                u => ProviderMetadataCatalog.GetCanonicalProviderId(u.ProviderId ?? string.Empty),
                StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in groups)
        {
            var cards = group.ToList();

            if (cards.Count == 1)
            {
                // Single card: flat checkbox with no heading.
                var usage = cards[0];
                var checkBox = new CheckBox
                {
                    Content = usage.ProviderName ?? usage.ProviderId,
                    Tag = usage.ProviderId,
                    IsChecked = !hidden.Contains(usage.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                    Margin = new Thickness(0, 2, 0, 6),
                    Foreground = (Brush)this.FindResource("SecondaryText"),
                };
                checkBox.Checked += this.ProviderVisibility_Changed;
                checkBox.Unchecked += this.ProviderVisibility_Changed;
                this.ProviderCardVisibilityPanel.Children.Add(checkBox);
            }
            else
            {
                // Multiple cards for one provider: bold heading + indented checkboxes.
                ProviderMetadataCatalog.TryGet(group.Key, out var definition);
                this.ProviderCardVisibilityPanel.Children.Add(new TextBlock
                {
                    Text = definition?.DisplayName ?? group.Key,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 4, 0, 4),
                    Foreground = (Brush)this.FindResource("SecondaryText"),
                });

                for (var i = 0; i < cards.Count; i++)
                {
                    var usage = cards[i];
                    var checkBox = new CheckBox
                    {
                        Content = usage.ProviderName ?? usage.ProviderId,
                        Tag = usage.ProviderId,
                        IsChecked = !hidden.Contains(usage.ProviderId ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                        Margin = new Thickness(15, 2, 0, i == cards.Count - 1 ? 16 : 2),
                        Foreground = (Brush)this.FindResource("SecondaryText"),
                    };
                    checkBox.Checked += this.ProviderVisibility_Changed;
                    checkBox.Unchecked += this.ProviderVisibility_Changed;
                    this.ProviderCardVisibilityPanel.Children.Add(checkBox);
                }
            }
        }
    }

    private void ProviderVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (!this.IsInitialized || sender is not CheckBox { Tag: string itemId } cb)
        {
            return;
        }

        this.SetHiddenProviderItemId(itemId, !(cb.IsChecked ?? true));
        this.ScheduleAutoSave();
    }

    private void SetHiddenProviderItemId(string id, bool hidden)
    {
        var list = this._preferences.HiddenProviderItemIds;
        if (hidden)
        {
            if (!list.Contains(id, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(id);
            }
        }
        else
        {
            foreach (var item in list.Where(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                list.Remove(item);
            }
        }
    }
}
