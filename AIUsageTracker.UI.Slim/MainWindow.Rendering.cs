// <copyright file="MainWindow.Rendering.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.UI.Slim.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class MainWindow : Window
{
    internal void RenderProviders()
    {
        this.LogDiagnostic("[DIAGNOSTIC] RenderProviders called");
        this.ProvidersList.Children.Clear();

        List<ProviderUsage> usagesCopy;
        lock (this._dataLock)
        {
            usagesCopy = this._usages?.ToList() ?? new List<ProviderUsage>();
        }

        this.LogDiagnostic($"[DIAGNOSTIC] ProvidersList cleared, _usages count: {usagesCopy.Count}");
        var renderPlan = MainWindowRuntimeLogic.BuildProviderRenderPlan(usagesCopy, this._preferences.HiddenProviderItemIds);
        this.LogDiagnostic(
            $"[DIAGNOSTIC] Provider render counts: raw={renderPlan.RawCount}, rendered={renderPlan.RenderedCount}");

        if (!string.IsNullOrWhiteSpace(renderPlan.Message))
        {
            this.LogDiagnostic($"[DIAGNOSTIC] {renderPlan.Message}");
            try
            {
                var messageBlock = this.CreateInfoTextBlock(renderPlan.Message);
                this.ProvidersList.Children.Add(messageBlock);
                this.ApplyProviderListFontPreferences();
                this.LogDiagnostic("[DIAGNOSTIC] Empty-state provider message added");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogError(ex, "Failed to add empty-state provider message");
            }

            return;
        }

        try
        {
            this.LogDiagnostic($"[DIAGNOSTIC] Rendering {renderPlan.RenderedCount} providers...");

            var cardRenderer = this.CreateProviderCardRenderer();
            foreach (var section in renderPlan.Sections)
            {
                var (header, container) = this.CreateCollapsibleHeader(
                    section.Title,
                    section.IsQuotaBased ? Brushes.DeepSkyBlue : Brushes.MediumSeaGreen,
                    isGroupHeader: true,
                    groupKey: section.SectionKey,
                    () => MainWindowRuntimeLogic.GetSectionIsCollapsed(this._preferences, section.IsQuotaBased),
                    v => MainWindowRuntimeLogic.SetSectionIsCollapsed(this._preferences, section.IsQuotaBased, v));

                this.ProvidersList.Children.Add(header);
                this.ProvidersList.Children.Add(container);

                var isCollapsed = MainWindowRuntimeLogic.GetSectionIsCollapsed(this._preferences, section.IsQuotaBased);
                if (isCollapsed)
                {
                    continue;
                }

                this.AddProviderCardsWithGrouping(section.Usages, container, cardRenderer);
            }

            this.ApplyProviderListFontPreferences();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this.LogDiagnostic($"[DIAGNOSTIC] RenderProviders failed: {ex}");
            this.ProvidersList.Children.Clear();
            this.ProvidersList.Children.Add(this.CreateInfoTextBlock("Failed to render provider cards. Check logs for details."));
            this.ApplyProviderListFontPreferences();
        }
    }

    private void ApplyProviderListFontPreferences()
    {
        if (this.ProvidersList == null)
        {
            return;
        }

        this.ApplyFontPreferencesToElement(this.ProvidersList);
    }

    private void ApplyFontPreferencesToElement(DependencyObject element)
    {
        if (element is TextBlock textBlock)
        {
            if (!string.IsNullOrWhiteSpace(this._preferences.FontFamily))
            {
                textBlock.FontFamily = new FontFamily(this._preferences.FontFamily);
            }

            if (this._preferences.FontSize > 0)
            {
                textBlock.FontSize = Math.Max(8, textBlock.FontSize * (this._preferences.FontSize / 12.0));
            }

            if (this._preferences.FontBold)
            {
                textBlock.FontWeight = FontWeights.Bold;
            }

            if (this._preferences.FontItalic)
            {
                textBlock.FontStyle = FontStyles.Italic;
            }
        }

        switch (element)
        {
            case Panel panel:
                foreach (UIElement child in panel.Children)
                {
                    this.ApplyFontPreferencesToElement(child);
                }

                break;

            case Border border when border.Child is not null:
                this.ApplyFontPreferencesToElement(border.Child);
                break;

            case Decorator decorator when decorator.Child is not null:
                this.ApplyFontPreferencesToElement(decorator.Child);
                break;

            case ContentControl contentControl when contentControl.Content is DependencyObject child:
                this.ApplyFontPreferencesToElement(child);
                break;
        }
    }

    private (UIElement Header, StackPanel Container) CreateCollapsibleHeader(
        string title,
        Brush accent,
        bool isGroupHeader,
        string? groupKey,
        Func<bool> getCollapsed,
        Action<bool> setCollapsed)
    {
        var margin = isGroupHeader
            ? new Thickness(0, 8, 0, 4)
            : new Thickness(20, 4, 0, 2);
        var toggleFontSize = isGroupHeader ? 10.0 : 9.0;
        var titleFontWeight = isGroupHeader ? FontWeights.Bold : FontWeights.Normal;
        var toggleOpacity = isGroupHeader ? 1.0 : 0.8;
        var lineOpacity = isGroupHeader ? 0.5 : 0.3;
        var titleText = isGroupHeader ? title.ToUpperInvariant() : title;
        var titleForeground = isGroupHeader
            ? accent
            : this.GetResourceBrush("SecondaryText", Brushes.Gray);

        var header = this.CreateCollapsibleHeaderGrid(margin);

        // Toggle button
        var toggleText = this.CreateText(
            getCollapsed() ? "\u25B6" : "\u25BC",
            toggleFontSize,
            accent,
            FontWeights.Bold,
            new Thickness(0, 0, 5, 0));
        toggleText.VerticalAlignment = VerticalAlignment.Center;
        toggleText.Opacity = toggleOpacity;
        toggleText.Tag = "ToggleIcon";

        // Title
        var titleBlock = this.CreateText(
            titleText,
            10.0,
            titleForeground,
            titleFontWeight,
            new Thickness(0, 0, 10, 0));
        titleBlock.VerticalAlignment = VerticalAlignment.Center;

        // Separator line
        var line = this.CreateSeparator(accent, lineOpacity);

        // Container
        var container = new StackPanel();
        if (!string.IsNullOrEmpty(groupKey))
        {
            container.Tag = $"{groupKey}Container";
        }

        container.Visibility = getCollapsed() ? Visibility.Collapsed : Visibility.Visible;

        // Click handler
        header.Cursor = System.Windows.Input.Cursors.Hand;
        header.MouseLeftButtonDown += async (s, e) =>
        {
            try
            {
                var newState = !getCollapsed();
                setCollapsed(newState);
                container.Visibility = newState ? Visibility.Collapsed : Visibility.Visible;
                toggleText.Text = newState ? "\u25B6" : "\u25BC";
                await this.SaveUiPreferencesAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                this._logger.LogWarning(ex, "Failed to save collapse state");
            }
        };

        Grid.SetColumn(toggleText, 0);
        Grid.SetColumn(titleBlock, 1);
        Grid.SetColumn(line, 2);

        header.Children.Add(toggleText);
        header.Children.Add(titleBlock);
        header.Children.Add(line);

        return (header, container);
    }

    private ProviderCardRenderer CreateProviderCardRenderer()
    {
        return new ProviderCardRenderer(
            this._preferences,
            this._isPrivacyMode,
            this.GetResourceBrush,
            this._createProviderIcon,
            this.CreateTopmostAwareToolTip,
            this.ConfigureCardToolTip,
            UsageMath.FormatRelativeTime);
    }

    private void AddProviderCardsWithGrouping(IReadOnlyList<ProviderUsage> usages, StackPanel container, ProviderCardRenderer cardRenderer)
    {
        // Group consecutive cards by GroupId for visual grouping.
        // Cards with GroupId == null are standalone and rendered directly.
        var i = 0;
        while (i < usages.Count)
        {
            var usage = usages[i];
            var groupId = usage.GroupId;

            if (string.IsNullOrEmpty(groupId))
            {
                this.AddProviderCard(usage, container, cardRenderer);
                i++;
                continue;
            }

            // Collect all cards in this group
            var groupCards = new List<ProviderUsage>();
            while (i < usages.Count && string.Equals(usages[i].GroupId, groupId, StringComparison.OrdinalIgnoreCase))
            {
                groupCards.Add(usages[i]);
                i++;
            }

            if (groupCards.Count == 1)
            {
                // Single card in group — render standalone
                this.AddProviderCard(groupCards[0], container, cardRenderer);
                continue;
            }

            // Multiple cards in a group — render first card normally, rest as children
            this.AddProviderCard(groupCards[0], container, cardRenderer, isChild: false);

            var (groupHeader, groupContainer) = this.CreateCollapsibleHeader(
                $"{ProviderMetadataCatalog.ResolveDisplayLabel(groupCards[0])} Details",
                System.Windows.Media.Brushes.DeepSkyBlue,
                isGroupHeader: false,
                groupKey: null,
                () => MainWindowRuntimeLogic.GetIsCollapsedForGroup(this._preferences, groupId),
                v => MainWindowRuntimeLogic.SetIsCollapsedForGroup(this._preferences, groupId, v));

            container.Children.Add(groupHeader);
            container.Children.Add(groupContainer);

            if (!MainWindowRuntimeLogic.GetIsCollapsedForGroup(this._preferences, groupId))
            {
                for (var j = 1; j < groupCards.Count; j++)
                {
                    this.AddProviderCard(groupCards[j], groupContainer, cardRenderer, isChild: true);
                }
            }
        }
    }

    private void AddProviderCard(ProviderUsage usage, StackPanel container, ProviderCardRenderer cardRenderer, bool isChild = false)
    {
        var showUsed = this.ShowUsedToggle?.IsChecked ?? false;
        var definition = ProviderMetadataCatalog.Find(usage.ProviderId ?? string.Empty);
        var card = cardRenderer.CreateProviderCard(usage, showUsed, isChild, definition);

        var contextMenu = new ContextMenu();
        var designerItem = new MenuItem { Header = "Card settings..." };
        designerItem.Click += (_, _) => this.OpenCardSettings();
        contextMenu.Items.Add(designerItem);
        card.ContextMenu = contextMenu;

        container.Children.Add(card);
    }

    private async void OpenCardSettings()
    {
        try
        {
            this._isSettingsDialogOpen = true;

            var owner = this.IsVisible ? this : null;
            var settingsWindow = App.Host.Services.GetRequiredService<SettingsWindow>();
            if (owner != null)
            {
                settingsWindow.Owner = owner;
                settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }

            settingsWindow.Loaded += (_, _) => settingsWindow.SelectTab("Cards");
            settingsWindow.ShowDialog();
        }
        finally
        {
            this._isSettingsDialogOpen = false;
            this.EnsureAlwaysOnTop();
        }

        this.ApplyPreferencesFromSettings();
        await this.InitializeAsync();
    }

    private ToolTip CreateTopmostAwareToolTip(FrameworkElement placementTarget, object content)
    {
        var toolTip = new ToolTip
        {
            Content = content,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            PlacementTarget = placementTarget,
        };

        toolTip.Opened += (s, e) =>
        {
            this._isTooltipOpen = true;
            if (s is ToolTip tip && tip.PlacementTarget != null)
            {
                var tooltipWindow = Window.GetWindow(tip);
                if (tooltipWindow != null && this.Topmost)
                {
                    tooltipWindow.Topmost = true;
                }
            }
        };
        toolTip.Closed += (s, e) => this._isTooltipOpen = false;

        return toolTip;
    }

    private void ConfigureCardToolTip(FrameworkElement target)
    {
        ToolTipService.SetInitialShowDelay(target, 100);
        ToolTipService.SetShowDuration(target, 15000);
    }

    private void ShowStatus(string message, StatusType type)
    {
        var effectiveMessage = message;
        var effectiveType = type;
        if (effectiveType == StatusType.Success &&
            !string.IsNullOrWhiteSpace(this._monitorContractWarningMessage))
        {
            effectiveMessage = this._monitorContractWarningMessage;
            effectiveType = StatusType.Warning;
        }

        var tooltipText = this._lastMonitorUpdate == DateTime.MinValue
            ? "Last update: Never"
            : $"Last update: {this._lastMonitorUpdate:HH:mm:ss}";

        var indicatorKind = effectiveType switch
        {
            StatusType.Success => StatusIndicatorKind.Success,
            StatusType.Warning => StatusIndicatorKind.Warning,
            StatusType.Error => StatusIndicatorKind.Error,
            _ => StatusIndicatorKind.Neutral,
        };

        var logLevel = effectiveType switch
        {
            StatusType.Error => LogLevel.Error,
            StatusType.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };

        if (this.StatusText != null)
        {
            this.StatusText.Text = effectiveMessage;
        }

        // Update LED color
        if (this.StatusLed != null)
        {
            this.StatusLed.Fill = indicatorKind switch
            {
                StatusIndicatorKind.Success => this.GetResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen),
                StatusIndicatorKind.Warning => Brushes.Gold,
                StatusIndicatorKind.Error => this.GetResourceBrush("ProgressBarRed", Brushes.Crimson),
                _ => this.GetResourceBrush("SecondaryText", Brushes.Gray),
            };
        }

        if (this.StatusLed != null)
        {
            this.StatusLed.ToolTip = this.CreateTopmostAwareToolTip(this.StatusLed, tooltipText);
        }

        if (this.StatusText != null)
        {
            this.StatusText.ToolTip = this.CreateTopmostAwareToolTip(this.StatusText, tooltipText);
        }

        this._logger.Log(
            logLevel,
            "[{StatusType}] {StatusMessage}",
            effectiveType,
            effectiveMessage);
    }

    private void ApplyMonitorContractStatus(AgentContractHandshakeResult handshakeResult)
    {
        if (handshakeResult.IsCompatible)
        {
            this._monitorContractWarningMessage = null;
            return;
        }

        this._monitorContractWarningMessage = handshakeResult.Message;
        this.ShowStatus(handshakeResult.Message, StatusType.Warning);
    }

    private void ShowErrorState(string message)
    {
        bool hasUsages;
        lock (this._dataLock)
        {
            hasUsages = this._usages.Any();
        }

        if (hasUsages)
        {
            // Preserve visible data and only surface status when we have a stale snapshot.
            this.ShowStatus(message, StatusType.Warning);
            return;
        }

        this.ProvidersList.Children.Clear();
        this.ProvidersList.Children.Add(this.CreateInfoTextBlock(message));
        this.ShowStatus(message, StatusType.Error);
    }

    private TextBlock CreateInfoTextBlock(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = this.GetResourceBrush("TertiaryText", Brushes.Gray),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10),
        };
    }

    // UI Element Creation Helpers
    private TextBlock CreateText(
        string text,
        double fontSize,
        Brush foreground,
        FontWeight? fontWeight = null,
        Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground,
            FontWeight = fontWeight ?? FontWeights.Normal,
            Margin = margin ?? new Thickness(0),
        };
    }

    private Border CreateSeparator(Brush color, double opacity = 0.5, double height = 1)
    {
        return new Border
        {
            Height = height,
            Background = color,
            Opacity = opacity,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private Grid CreateCollapsibleHeaderGrid(Thickness margin)
    {
        var header = new Grid { Margin = margin };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return header;
    }
}
