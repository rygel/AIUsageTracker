// <copyright file="ProviderCardRenderer.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

/// <summary>
/// Builds provider and sub-provider card visuals for the Slim UI.
/// Keeps imperative card rendering out of MainWindow code-behind.
/// </summary>
internal sealed class ProviderCardRenderer
{
    private const string ResourceKeyTertiaryText = "TertiaryText";
    private const string ResourceKeySecondaryText = "SecondaryText";
    private const string ResourceKeyProgressBarRed = "ProgressBarRed";
    private const string ResourceKeyProgressBarGreen = "ProgressBarGreen";

    private readonly AppPreferences _preferences;
    private readonly bool _isPrivacyMode;
    private readonly Func<string, SolidColorBrush, SolidColorBrush> _getResourceBrush;
    private readonly Func<string, FrameworkElement> _createProviderIcon;
    private readonly Func<FrameworkElement, object, ToolTip> _createToolTip;
    private readonly Action<FrameworkElement> _configureToolTip;
    private readonly Func<DateTime, string> _getRelativeTimeString;

    public ProviderCardRenderer(
        AppPreferences preferences,
        bool isPrivacyMode,
        Func<string, SolidColorBrush, SolidColorBrush> getResourceBrush,
        Func<string, FrameworkElement> createProviderIcon,
        Func<FrameworkElement, object, ToolTip> createToolTip,
        Action<FrameworkElement> configureToolTip,
        Func<DateTime, string> getRelativeTimeString)
    {
        this._preferences = preferences;
        this._isPrivacyMode = isPrivacyMode;
        this._getResourceBrush = getResourceBrush;
        this._createProviderIcon = createProviderIcon;
        this._createToolTip = createToolTip;
        this._configureToolTip = configureToolTip;
        this._getRelativeTimeString = getRelativeTimeString;
    }

    public FrameworkElement CreateProviderCard(ProviderUsage usage, bool showUsed, bool isChild = false, ProviderDefinition? definition = null)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var friendlyName = usage.ProviderName ?? providerId;

        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed, this._preferences.EnablePaceAdjustment);

        var isCompact = this._preferences.CardCompactMode;
        var grid = new Grid
        {
            Margin = new Thickness(isChild ? 20 : 0, 0, 0, isCompact ? 1 : 2),
            Height = isCompact ? 20 : 24,
            Background = Brushes.Transparent,
            Tag = providerId,
        };

        var pGrid = new Grid();
        var useDualBars = presentation.DualBar != null && this._preferences.ShowDualQuotaBars;
        if (useDualBars)
        {
            this.BuildDualProgressBar(pGrid, presentation, showUsed);
        }
        else
        {
            this.BuildSingleProgressBar(pGrid, presentation, showUsed);
        }

        var cardPaceColor = presentation.DualBar?.Secondary.PaceColor ?? presentation.PaceColor;

        this.AddCardBackground(grid, pGrid, presentation, cardPaceColor);

        var contentPadding = isCompact ? 4 : 6;
        var contentPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(contentPadding, 0, contentPadding, 0) };
        this.AddCardIcon(contentPanel, providerId, isChild, isCompact);

        // Right-side slots rendered from right to left (rightmost = first added)
        this.RenderSlot(contentPanel, this._preferences.CardResetInfo, usage, presentation, showUsed, cardPaceColor);
        this.RenderSlot(contentPanel, this._preferences.CardPrimaryBadge, usage, presentation, showUsed, cardPaceColor);
        this.RenderSlot(contentPanel, this._preferences.CardSecondaryBadge, usage, presentation, showUsed, cardPaceColor);
        this.RenderSlot(contentPanel, this._preferences.CardStatusLine, usage, presentation, showUsed, cardPaceColor);

        var accountName = MainWindowRuntimeLogic.ResolveDisplayAccountName(
            providerId,
            usage.AccountName,
            this._isPrivacyMode,
            definition);
        AddDockedElement(
            contentPanel,
            this.CreateProviderNameTextBlock(
                friendlyName,
                accountName,
                presentation.IsMissing,
                isChild),
            Dock.Left);

        this.AddStatusBadges(contentPanel, presentation);
        grid.Children.Add(contentPanel);

        if (presentation.IsStale)
        {
            grid.Opacity = 0.55;
        }

        this.AttachTooltip(grid, usage, friendlyName);

        return grid;
    }

    private void AddStatusBadges(DockPanel contentPanel, ProviderCardPresentation presentation)
    {
        if (presentation.IsExpired)
        {
            AddDockedElement(
                contentPanel,
                this.CreateDockedTextBlock(
                    "Expired",
                    fontSize: 9,
                    foreground: Brushes.Orange,
                    fontWeight: FontWeights.SemiBold,
                    margin: new Thickness(6, 0, 0, 0)),
                Dock.Right);
        }

        if (presentation.IsStale)
        {
            AddDockedElement(
                contentPanel,
                this.CreateDockedTextBlock(
                    "Stale",
                    fontSize: 9,
                    foreground: Brushes.IndianRed,
                    fontWeight: FontWeights.SemiBold,
                    margin: new Thickness(6, 0, 0, 0)),
                Dock.Right);
        }
    }

    private void AttachTooltip(Grid grid, ProviderUsage usage, string friendlyName)
    {
        var toolTipContent = MainWindowRuntimeLogic.BuildTooltipContent(
            usage,
            friendlyName,
            this._preferences.UseRelativeResetTime);
        if (!string.IsNullOrEmpty(toolTipContent))
        {
            grid.ToolTip = this._createToolTip(grid, toolTipContent);
            this._configureToolTip(grid);
        }
    }

    private void AddCardIcon(DockPanel contentPanel, string providerId, bool isChild, bool isCompact)
    {
        if (isChild)
        {
            AddDockedElement(contentPanel, this.CreateBulletMarker(), Dock.Left);
            return;
        }

        var iconSize = isCompact ? 12 : 14;
        var providerIcon = this._createProviderIcon(providerId);
        providerIcon.Margin = new Thickness(0, 0, isCompact ? 4 : 6, 0);
        providerIcon.Width = iconSize;
        providerIcon.Height = iconSize;
        providerIcon.VerticalAlignment = VerticalAlignment.Center;
        AddDockedElement(contentPanel, providerIcon, Dock.Left);
    }

    private void AddCardBackground(Grid grid, Grid pGrid, ProviderCardPresentation presentation, PaceColorResult cardPaceColor)
    {
        var useBackgroundBar = this._preferences.CardBackgroundBar;
        if (useBackgroundBar)
        {
            pGrid.Visibility = presentation.ShouldHaveProgress ? Visibility.Visible : Visibility.Collapsed;
            grid.Children.Add(pGrid);

            var bg = new Border
            {
                Background = this._getResourceBrush("CardBackground", Brushes.DarkGray),
                CornerRadius = new CornerRadius(0),
                Visibility = presentation.ShouldHaveProgress ? Visibility.Collapsed : Visibility.Visible,
            };
            grid.Children.Add(bg);
        }
        else
        {
            grid.Children.Add(new Border
            {
                Background = this._getResourceBrush("CardBackground", Brushes.DarkGray),
            });

            if (presentation.ShouldHaveProgress)
            {
                grid.Children.Add(new Border
                {
                    Width = 3,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = this.GetProgressBarColor(cardPaceColor),
                    Opacity = 0.8,
                });
            }
        }
    }

    private void BuildDualProgressBar(Grid pGrid, ProviderCardPresentation presentation, bool showUsed)
    {
        pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var primaryRow = this.CreateProgressLayer(presentation.DualBar!.Primary.UsedPercent, presentation.DualBar.Primary.PaceColor, showUsed, opacity: 0.55);
        var secondaryRow = this.CreateProgressLayer(presentation.DualBar.Secondary.UsedPercent, presentation.DualBar.Secondary.PaceColor, showUsed, opacity: 0.35);
        Grid.SetRow(primaryRow, 0);
        Grid.SetRow(secondaryRow, 1);
        pGrid.Children.Add(primaryRow);
        pGrid.Children.Add(secondaryRow);

        if (!string.IsNullOrEmpty(presentation.DualBar.Primary.Label))
        {
            var burstLabel = new TextBlock
            {
                Text = presentation.DualBar.Primary.Label,
                FontSize = 8,
                Foreground = this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(3, 0, 0, 0),
                Opacity = 0.8,
            };
            Grid.SetRow(burstLabel, 0);
            pGrid.Children.Add(burstLabel);
        }

        if (!string.IsNullOrEmpty(presentation.DualBar.Secondary.Label))
        {
            var weeklyLabel = new TextBlock
            {
                Text = presentation.DualBar.Secondary.Label,
                FontSize = 8,
                Foreground = this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(3, 0, 0, 0),
                Opacity = 0.8,
            };
            Grid.SetRow(weeklyLabel, 1);
            pGrid.Children.Add(weeklyLabel);
        }
    }

    private void BuildSingleProgressBar(Grid pGrid, ProviderCardPresentation presentation, bool showUsed)
    {
        double indicatorWidth;
        PaceColorResult colorIndicator;

        if (presentation.DualBar != null && !this._preferences.ShowDualQuotaBars)
        {
            var bar = this._preferences.DualQuotaSingleBarMode == DualQuotaSingleBarMode.Burst
                ? presentation.DualBar.Primary
                : presentation.DualBar.Secondary;
            indicatorWidth = showUsed ? bar.UsedPercent : Math.Max(0, 100 - bar.UsedPercent);
            colorIndicator = bar.PaceColor;
        }
        else
        {
            indicatorWidth = showUsed ? presentation.UsedPercent : presentation.RemainingPercent;
            colorIndicator = presentation.PaceColor;
        }

        var layer = this.CreateSingleProgressLayer(colorIndicator, indicatorWidth, opacity: 0.45);
        pGrid.Children.Add(layer);
    }

    private Grid CreateProgressLayer(double usedPercent, PaceColorResult paceColor, bool showUsed, double opacity)
    {
        var remainingPercent = Math.Max(0, 100 - usedPercent);
        var indicatorWidth = showUsed ? usedPercent : remainingPercent;
        return this.CreateSingleProgressLayer(paceColor, indicatorWidth, opacity);
    }

    private Grid CreateSingleProgressLayer(PaceColorResult paceColor, double indicatorWidth, double opacity)
    {
        var clampedWidth = Math.Clamp(indicatorWidth, 0, 100);

        var layer = new Grid();
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clampedWidth, GridUnitType.Star) });
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - clampedWidth), GridUnitType.Star) });

        layer.Children.Add(new Border
        {
            Background = this.GetProgressBarColor(paceColor),
            Opacity = opacity,
            CornerRadius = new CornerRadius(0),
        });

        return layer;
    }

    private Brush GetProgressBarColor(PaceColorResult paceColor)
    {
        if (paceColor.IsPaceAdjusted)
        {
            // Tier is the single source of truth: Headroom/OnPace → green, OverPace → red.
            return paceColor.PaceTier == PaceTier.OverPace
                ? this._getResourceBrush(ResourceKeyProgressBarRed, Brushes.Crimson)
                : this._getResourceBrush(ResourceKeyProgressBarGreen, Brushes.MediumSeaGreen);
        }

        return this.GetProgressBarColor(paceColor.ColorPercent);
    }

    private Brush GetProgressBarColor(double usedPercentage)
    {
        var yellowThreshold = this._preferences.ColorThresholdYellow;
        var redThreshold = this._preferences.ColorThresholdRed;

        if (usedPercentage >= redThreshold)
        {
            return this._getResourceBrush(ResourceKeyProgressBarRed, Brushes.Crimson);
        }

        if (usedPercentage >= yellowThreshold)
        {
            return this._getResourceBrush("ProgressBarYellow", Brushes.Gold);
        }

        return this._getResourceBrush(ResourceKeyProgressBarGreen, Brushes.MediumSeaGreen);
    }

    private string? BuildResetBadgeText(ProviderUsage usage, ProviderCardPresentation presentation)
    {
        var (resetTime, resetLabel) = this.ResolveDisplayedReset(usage, presentation);
        if (!resetTime.HasValue)
        {
            return null;
        }

        return FormatResetText(resetTime.Value, resetLabel, this._getRelativeTimeString);
    }

    private void RenderSlot(
        DockPanel panel,
        CardSlotContent slot,
        ProviderUsage usage,
        ProviderCardPresentation presentation,
        bool showUsed,
        PaceColorResult paceColor)
    {
        if (slot == CardSlotContent.None)
        {
            return;
        }

        switch (slot)
        {
            case CardSlotContent.PaceBadge:
                this.RenderPaceBadgeSlot(panel, paceColor);
                break;
            case CardSlotContent.ProjectedPercent:
                this.RenderProjectedPercent(panel, paceColor);
                break;
            case CardSlotContent.DailyBudget:
                this.RenderDailyBudget(panel, usage);
                break;
            case CardSlotContent.UsageRate:
                this.RenderUsageRate(panel, usage);
                break;
            case CardSlotContent.UsedPercent:
                this.AddSlotText(panel, GetUsedSlotText(usage), this._getResourceBrush(ResourceKeySecondaryText, Brushes.Gray), 10);
                break;
            case CardSlotContent.RemainingPercent:
                this.AddSlotText(panel, GetRemainingSlotText(usage), this._getResourceBrush(ResourceKeySecondaryText, Brushes.Gray), 10);
                break;
            case CardSlotContent.ResetAbsolute:
            case CardSlotContent.ResetAbsoluteDate:
            case CardSlotContent.ResetRelative:
                this.RenderResetSlot(panel, slot, usage, presentation);
                break;
            case CardSlotContent.StatusText:
                this.RenderStatusTextSlot(panel, presentation, showUsed);
                break;
            case CardSlotContent.AccountName:
                this.RenderAccountName(panel, usage);
                break;
            case CardSlotContent.AuthSource:
                this.RenderAuthSource(panel, usage);
                break;
        }
    }

    private void RenderProjectedPercent(DockPanel panel, PaceColorResult paceColor)
    {
        if (paceColor.IsPaceAdjusted)
        {
            this.AddSlotText(panel, $"Projected: {paceColor.ProjectedPercent.ToString("F0", CultureInfo.InvariantCulture)}%", this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray), 9);
        }
    }

    private void RenderDailyBudget(DockPanel panel, ProviderUsage usage)
    {
        if (usage.PeriodDuration.HasValue && usage.PeriodDuration.Value.TotalDays >= 1)
        {
            var dailyBudget = 100.0 / usage.PeriodDuration.Value.TotalDays;
            this.AddSlotText(panel, $"{dailyBudget.ToString("F0", CultureInfo.InvariantCulture)}%/day budget", this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray), 9);
        }
    }

    private void RenderUsageRate(DockPanel panel, ProviderUsage usage)
    {
        if (this._preferences.ShowUsagePerHour && usage.UsagePerHour.HasValue)
        {
            this.AddSlotText(panel, $"{usage.UsagePerHour.Value:F1}/hr", this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray), 9);
        }
    }

    private void RenderAccountName(DockPanel panel, ProviderUsage usage)
    {
        if (!string.IsNullOrWhiteSpace(usage.AccountName))
        {
            var name = this._isPrivacyMode ? "****" : usage.AccountName;
            this.AddSlotText(panel, name, this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray), 9);
        }
    }

    private void RenderAuthSource(DockPanel panel, ProviderUsage usage)
    {
        if (!string.IsNullOrWhiteSpace(usage.AuthSource))
        {
            this.AddSlotText(panel, usage.AuthSource, this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray), 9);
        }
    }

    private void RenderPaceBadgeSlot(DockPanel panel, PaceColorResult paceColor)
    {
        if (!paceColor.IsPaceAdjusted)
        {
            return;
        }

        var badgeBrush = paceColor.PaceTier switch
        {
            PaceTier.OverPace => this._getResourceBrush(ResourceKeyProgressBarRed, Brushes.IndianRed),
            _ => this._getResourceBrush(ResourceKeyProgressBarGreen, Brushes.MediumSeaGreen),
        };
        this.AddSlotText(panel, paceColor.ProjectedText, this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray), 9);
        this.AddSlotText(panel, paceColor.BadgeText, badgeBrush, 9, FontWeights.SemiBold);
    }

    private void RenderStatusTextSlot(DockPanel panel, ProviderCardPresentation presentation, bool showUsed)
    {
        var statusText = presentation.DualBar != null && !this._preferences.ShowDualQuotaBars
            ? MainWindowRuntimeLogic.BuildSingleDualQuotaStatusText(
                presentation, showUsed, this._preferences.DualQuotaSingleBarMode)
            : presentation.StatusText;

        Brush statusBrush = presentation.StatusTone switch
        {
            ProviderCardStatusTone.Missing => Brushes.IndianRed,
            ProviderCardStatusTone.Warning => Brushes.Orange,
            ProviderCardStatusTone.Error => Brushes.Red,
            _ => this._getResourceBrush(ResourceKeySecondaryText, Brushes.Gray),
        };
        this.AddSlotText(panel, statusText, statusBrush, 10);
    }

    private void RenderResetSlot(DockPanel panel, CardSlotContent slot, ProviderUsage usage, ProviderCardPresentation presentation)
    {
        var effectiveSlot = slot;
        if (this._preferences.UseRelativeResetTime && slot == CardSlotContent.ResetAbsolute)
        {
            effectiveSlot = CardSlotContent.ResetRelative;
        }

        var (resetTime, resetLabel) = this.ResolveDisplayedReset(usage, presentation);
        if (!resetTime.HasValue)
        {
            return;
        }

        var resetText = this.BuildResetBadgeText(usage, presentation);
        if (string.IsNullOrWhiteSpace(resetText))
        {
            return;
        }

        if (effectiveSlot == CardSlotContent.ResetRelative)
        {
            resetText = FormatResetText(resetTime.Value, resetLabel, UsageMath.FormatRelativeTime);
        }
        else if (effectiveSlot == CardSlotContent.ResetAbsoluteDate)
        {
            resetText = FormatResetText(resetTime.Value, resetLabel, UsageMath.FormatAbsoluteDate);
        }

        this.AddSlotText(panel, resetText, this._getResourceBrush("StatusTextWarning", Brushes.Goldenrod), 10, FontWeights.SemiBold);
    }

    private (DateTime? ResetTime, string? ResetLabel) ResolveDisplayedReset(
        ProviderUsage usage,
        ProviderCardPresentation presentation)
    {
        return MainWindowRuntimeLogic.ResolveCardResetDisplay(
            usage,
            presentation,
            this._preferences.ShowDualQuotaBars,
            this._preferences.DualQuotaSingleBarMode);
    }

    private static string FormatResetText(DateTime resetTime, string? resetLabel, Func<DateTime, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        var formattedReset = formatter(resetTime);
        return string.IsNullOrWhiteSpace(resetLabel)
            ? $"({formattedReset})"
            : $"({resetLabel}: {formattedReset})";
    }

    private static string GetUsedSlotText(ProviderUsage usage)
    {
        if (usage.IsCurrencyUsage)
        {
            return $"${usage.RequestsUsed.ToString("F2", CultureInfo.InvariantCulture)} used";
        }

        return $"{usage.UsedPercent.ToString("F0", CultureInfo.InvariantCulture)}% used";
    }

    private static string GetRemainingSlotText(ProviderUsage usage)
    {
        if (usage.IsCurrencyUsage)
        {
            var remaining = Math.Max(0, usage.RequestsAvailable - usage.RequestsUsed);
            return $"${remaining.ToString("F2", CultureInfo.InvariantCulture)} remaining";
        }

        return $"{Math.Max(0, 100 - usage.UsedPercent).ToString("F0", CultureInfo.InvariantCulture)}% remaining";
    }

    private void AddSlotText(DockPanel panel, string text, Brush foreground, double fontSize, FontWeight? fontWeight = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var compact = this._preferences.CardCompactMode;
        AddDockedElement(
            panel,
            this.CreateDockedTextBlock(
                text,
                fontSize: compact ? fontSize - 1 : fontSize,
                foreground: foreground,
                fontWeight: fontWeight ?? FontWeights.Normal,
                margin: new Thickness(compact ? 4 : 6, 0, 0, 0)),
            Dock.Right);
    }

    private Border CreateBulletMarker()
    {
        return new Border
        {
            Width = 4,
            Height = 4,
            Background = this._getResourceBrush(ResourceKeySecondaryText, Brushes.Gray),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(2, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private TextBlock CreateProviderNameTextBlock(
        string providerName,
        string accountName,
        bool isMissing,
        bool isChild)
    {
        var primaryTextBrush = isMissing
            ? this._getResourceBrush(ResourceKeyTertiaryText, Brushes.Gray)
            : this._getResourceBrush("PrimaryText", Brushes.White);
        var secondaryTextBrush = this._getResourceBrush(ResourceKeySecondaryText, Brushes.Gray);

        var textBlock = new TextBlock
        {
            FontSize = 11,
            Foreground = primaryTextBrush,
            FontWeight = isChild ? FontWeights.Normal : FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };

        textBlock.Inlines.Add(new Run(providerName));

        if (!string.IsNullOrWhiteSpace(accountName))
        {
            textBlock.Inlines.Add(new Run($" [{accountName}]")
            {
                Foreground = secondaryTextBrush,
                FontWeight = FontWeights.Normal,
                FontStyle = FontStyles.Italic,
            });
        }

        return textBlock;
    }

    private TextBlock CreateDockedTextBlock(
        string text,
        double fontSize,
        Brush foreground,
        FontWeight? fontWeight = null,
        Thickness? margin = null,
        TextTrimming textTrimming = TextTrimming.None)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = foreground,
            FontWeight = fontWeight ?? FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = margin ?? new Thickness(0),
            TextTrimming = textTrimming,
        };
    }

    private static void AddDockedElement(DockPanel panel, UIElement element, Dock dock)
    {
        panel.Children.Add(element);
        DockPanel.SetDock(element, dock);
    }
}
