// <copyright file="ProviderCardRenderer.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

/// <summary>
/// Builds provider and sub-provider card visuals for the Slim UI.
/// Keeps imperative card rendering out of MainWindow code-behind.
/// </summary>
internal sealed class ProviderCardRenderer
{
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

    public FrameworkElement CreateProviderCard(ProviderUsage usage, bool showUsed, bool isChild = false)
    {
        var providerId = usage.ProviderId ?? string.Empty;
        var friendlyName = ProviderMetadataCatalog.ResolveDisplayLabel(usage);
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed, this._preferences.ColorThresholdRed);

        var isCompact = this._preferences.CardCompactMode;
        var grid = new Grid
        {
            Margin = new Thickness(isChild ? 20 : 0, 0, 0, isCompact ? 1 : 2),
            Height = isCompact ? 20 : 24,
            Background = Brushes.Transparent,
            Tag = providerId,
        };

        var pGrid = new Grid();
        var useDualBars = presentation.HasDualBuckets && this._preferences.ShowDualQuotaBars;
        if (useDualBars)
        {
            pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var primaryRow = this.CreateProgressLayer(presentation.DualBucketPrimaryUsed!.Value, showUsed, opacity: 0.55);
            var secondaryRow = this.CreateProgressLayer(presentation.DualBucketSecondaryUsed!.Value, showUsed, opacity: 0.35);
            Grid.SetRow(primaryRow, 0);
            Grid.SetRow(secondaryRow, 1);
            pGrid.Children.Add(primaryRow);
            pGrid.Children.Add(secondaryRow);

            // Burst/weekly labels on each bar row (from provider metadata)
            if (!string.IsNullOrEmpty(presentation.DualBucketPrimaryLabel))
            {
                var burstLabel = new TextBlock
                {
                    Text = presentation.DualBucketPrimaryLabel,
                    FontSize = 8,
                    Foreground = this._getResourceBrush("TertiaryText", Brushes.Gray),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3, 0, 0, 0),
                    Opacity = 0.8,
                };
                Grid.SetRow(burstLabel, 0);
                pGrid.Children.Add(burstLabel);
            }

            if (!string.IsNullOrEmpty(presentation.DualBucketSecondaryLabel))
            {
                var weeklyLabel = new TextBlock
                {
                    Text = presentation.DualBucketSecondaryLabel,
                    FontSize = 8,
                    Foreground = this._getResourceBrush("TertiaryText", Brushes.Gray),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(3, 0, 0, 0),
                    Opacity = 0.8,
                };
                Grid.SetRow(weeklyLabel, 1);
                pGrid.Children.Add(weeklyLabel);
            }
        }
        else
        {
            double indicatorWidth;
            double colorIndicatorPercent;

            if (presentation.HasDualBuckets && !this._preferences.ShowDualQuotaBars)
            {
                var (selectedUsed, selectedColor) = this.GetSingleBarDualQuotaPercents(presentation);
                indicatorWidth = showUsed ? selectedUsed : Math.Max(0, 100 - selectedUsed);
                colorIndicatorPercent = selectedColor;
            }
            else
            {
                var paceColor = UsageMath.ComputePaceColor(
                    presentation.UsedPercent,
                    usage.NextResetTime,
                    usage.PeriodDuration,
                    this._preferences.ColorThresholdRed,
                    this._preferences.EnablePaceAdjustment);
                indicatorWidth = showUsed ? presentation.UsedPercent : presentation.RemainingPercent;
                colorIndicatorPercent = paceColor.ColorPercent;
            }
            pGrid = this.CreateSingleProgressLayer(colorIndicatorPercent, indicatorWidth, opacity: 0.45);
        }

        // Compute pace result once — used by both bar color and slot rendering.
        var cardPaceColor = UsageMath.ComputePaceColor(
            presentation.UsedPercent,
            usage.NextResetTime,
            usage.PeriodDuration,
            this._preferences.ColorThresholdRed,
            this._preferences.EnablePaceAdjustment);

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
            // Color-indicator-only mode: thin color stripe on the left edge
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
                    Background = this.GetProgressBarColor(cardPaceColor.ColorPercent),
                    Opacity = 0.8,
                });
            }
        }

        var contentPadding = isCompact ? 4 : 6;
        var contentPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(contentPadding, 0, contentPadding, 0) };
        if (isChild)
        {
            AddDockedElement(contentPanel, this.CreateBulletMarker(), Dock.Left);
        }
        else
        {
            var iconSize = isCompact ? 12 : 14;
            var providerIcon = this._createProviderIcon(providerId);
            providerIcon.Margin = new Thickness(0, 0, isCompact ? 4 : 6, 0);
            providerIcon.Width = iconSize;
            providerIcon.Height = iconSize;
            providerIcon.VerticalAlignment = VerticalAlignment.Center;
            AddDockedElement(contentPanel, providerIcon, Dock.Left);
        }

        // Right-side slots rendered from right to left (rightmost = first added)
        this.RenderSlot(contentPanel, this._preferences.CardResetInfo, usage, presentation, showUsed, cardPaceColor);
        this.RenderSlot(contentPanel, this._preferences.CardPrimaryBadge, usage, presentation, showUsed, cardPaceColor);
        this.RenderSlot(contentPanel, this._preferences.CardSecondaryBadge, usage, presentation, showUsed, cardPaceColor);
        this.RenderSlot(contentPanel, this._preferences.CardStatusLine, usage, presentation, showUsed, cardPaceColor);

        var accountName = MainWindowRuntimeLogic.ResolveDisplayAccountName(
            providerId,
            usage.AccountName,
            this._isPrivacyMode);
        AddDockedElement(
            contentPanel,
            this.CreateProviderNameTextBlock(
                friendlyName,
                accountName,
                presentation.IsMissing,
                isChild),
            Dock.Left);

        if (presentation.IsStale)
        {
            // Add visible "Stale" badge before the provider name
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

        grid.Children.Add(contentPanel);

        if (presentation.IsStale)
        {
            grid.Opacity = 0.55;
        }

        var toolTipContent = MainWindowRuntimeLogic.BuildTooltipContent(usage, friendlyName);
        if (!string.IsNullOrEmpty(toolTipContent))
        {
            grid.ToolTip = this._createToolTip(grid, toolTipContent);
            this._configureToolTip(grid);
        }

        return grid;
    }

    public FrameworkElement CreateSubProviderCard(ProviderUsage usage, ProviderUsageDetail detail, bool showUsed)
    {
        var grid = new Grid
        {
            Margin = new Thickness(20, 0, 0, 2),
            Height = 20,
            Background = Brushes.Transparent,
        };

        var presentation = MainWindowRuntimeLogic.BuildDetailPresentation(
            detail,
            showUsed,
            this._getRelativeTimeString);

        var pGrid = this.CreateSingleProgressLayer(presentation.UsedPercent, presentation.IndicatorWidth, opacity: 0.3);
        if (presentation.HasProgress)
        {
            grid.Children.Add(pGrid);
        }

        var bulletPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };
        AddDockedElement(bulletPanel, this.CreateBulletMarker(), Dock.Left);

        if (!string.IsNullOrEmpty(presentation.ResetText))
        {
            AddDockedElement(
                bulletPanel,
                this.CreateDockedTextBlock(
                    presentation.ResetText,
                    fontSize: 9,
                    foreground: this._getResourceBrush("StatusTextWarning", Brushes.Goldenrod),
                    fontWeight: FontWeights.SemiBold,
                    margin: new Thickness(6, 0, 0, 0)),
                Dock.Right);
        }

        AddDockedElement(
            bulletPanel,
            this.CreateDockedTextBlock(
                presentation.DisplayText,
                fontSize: 10,
                foreground: this._getResourceBrush("TertiaryText", Brushes.Gray),
                margin: new Thickness(10, 0, 0, 0)),
            Dock.Right);

        AddDockedElement(
            bulletPanel,
            this.CreateDockedTextBlock(
                detail.Name,
                fontSize: 10,
                foreground: this._getResourceBrush("SecondaryText", Brushes.LightGray),
                textTrimming: TextTrimming.CharacterEllipsis),
            Dock.Left);

        grid.Children.Add(bulletPanel);
        return grid;
    }

    private Grid CreateProgressLayer(double usedPercent, bool showUsed, double opacity)
    {
        var remainingPercent = Math.Max(0, 100 - usedPercent);
        var indicatorWidth = showUsed ? usedPercent : remainingPercent;
        return this.CreateSingleProgressLayer(usedPercent, indicatorWidth, opacity);
    }

    private Grid CreateSingleProgressLayer(double usedPercent, double indicatorWidth, double opacity)
    {
        var clampedWidth = Math.Clamp(indicatorWidth, 0, 100);

        var layer = new Grid();
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(clampedWidth, GridUnitType.Star) });
        layer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.001, 100 - clampedWidth), GridUnitType.Star) });

        layer.Children.Add(new Border
        {
            Background = this.GetProgressBarColor(usedPercent),
            Opacity = opacity,
            CornerRadius = new CornerRadius(0),
        });

        return layer;
    }

    private Brush GetProgressBarColor(double usedPercentage)
    {
        var yellowThreshold = this._preferences.ColorThresholdYellow;
        var redThreshold = this._preferences.ColorThresholdRed;

        if (usedPercentage >= redThreshold)
        {
            return this._getResourceBrush("ProgressBarRed", Brushes.Crimson);
        }

        if (usedPercentage >= yellowThreshold)
        {
            return this._getResourceBrush("ProgressBarYellow", Brushes.Gold);
        }

        return this._getResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen);
    }

    private string? BuildResetBadgeText(ProviderUsage usage, ProviderCardPresentation presentation)
    {
        IReadOnlyList<DateTime> resetTimes;
        if (presentation.HasDualBuckets && !this._preferences.ShowDualQuotaBars)
        {
            var preferredKind = MainWindowRuntimeLogic.GetPreferredDualBucketKind(
                presentation,
                this._preferences.DualQuotaSingleBarMode);
            resetTimes = preferredKind.HasValue
                ? MainWindowRuntimeLogic.ResolveResetTimesForWindow(usage, preferredKind.Value)
                : Array.Empty<DateTime>();
        }
        else
        {
            resetTimes = MainWindowRuntimeLogic.ResolveResetTimes(
                usage,
                presentation.SuppressSingleResetTime);
        }

        if (resetTimes.Count == 0)
        {
            return null;
        }

        var resetParts = resetTimes
            .Select(this._getRelativeTimeString)
            .ToList();
        return $"({string.Join(" | ", resetParts)})";
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
                if (paceColor.IsPaceAdjusted)
                {
                    var badgeBrush = paceColor.PaceTier switch
                    {
                        PaceTier.OverPace => this._getResourceBrush("ProgressBarRed", Brushes.IndianRed),
                        _ => this._getResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen),
                    };
                    this.AddSlotText(panel, paceColor.ProjectedText, this._getResourceBrush("TertiaryText", Brushes.Gray), 9);
                    this.AddSlotText(panel, paceColor.BadgeText, badgeBrush, 9, FontWeights.SemiBold);
                }

                break;

            case CardSlotContent.ProjectedPercent:
                if (paceColor.IsPaceAdjusted)
                {
                    this.AddSlotText(panel, $"Projected: {paceColor.ProjectedPercent:F0}%", this._getResourceBrush("TertiaryText", Brushes.Gray), 9);
                }

                break;

            case CardSlotContent.DailyBudget:
                if (usage.PeriodDuration.HasValue && usage.PeriodDuration.Value.TotalDays >= 1)
                {
                    var dailyBudget = 100.0 / usage.PeriodDuration.Value.TotalDays;
                    this.AddSlotText(panel, $"{dailyBudget:F0}%/day budget", this._getResourceBrush("TertiaryText", Brushes.Gray), 9);
                }

                break;

            case CardSlotContent.UsageRate:
                if (this._preferences.ShowUsagePerHour && usage.UsagePerHour.HasValue)
                {
                    this.AddSlotText(panel, $"{usage.UsagePerHour.Value:F1}/hr", this._getResourceBrush("TertiaryText", Brushes.Gray), 9);
                }

                break;

            case CardSlotContent.UsedPercent:
                this.AddSlotText(panel, $"{usage.UsedPercent:F0}% used", this._getResourceBrush("SecondaryText", Brushes.Gray), 10);
                break;

            case CardSlotContent.RemainingPercent:
                this.AddSlotText(panel, $"{Math.Max(0, 100 - usage.UsedPercent):F0}% remaining", this._getResourceBrush("SecondaryText", Brushes.Gray), 10);
                break;

            case CardSlotContent.ResetAbsolute:
            case CardSlotContent.ResetAbsoluteDate:
            case CardSlotContent.ResetRelative:
                // Determine the effective format: the slot type sets the default,
                // but UseRelativeResetTime overrides ResetAbsolute to relative.
                var effectiveSlot = slot;
                if (this._preferences.UseRelativeResetTime && slot == CardSlotContent.ResetAbsolute)
                {
                    effectiveSlot = CardSlotContent.ResetRelative;
                }

                var resetText = this.BuildResetBadgeText(usage, presentation);
                if (!string.IsNullOrWhiteSpace(resetText))
                {
                    if (effectiveSlot == CardSlotContent.ResetRelative && usage.NextResetTime.HasValue)
                    {
                        resetText = $"({UsageMath.FormatRelativeTime(usage.NextResetTime.Value)})";
                    }
                    else if (effectiveSlot == CardSlotContent.ResetAbsoluteDate && usage.NextResetTime.HasValue)
                    {
                        resetText = $"({UsageMath.FormatAbsoluteDate(usage.NextResetTime.Value)})";
                    }

                    this.AddSlotText(panel, resetText, this._getResourceBrush("StatusTextWarning", Brushes.Goldenrod), 10, FontWeights.SemiBold);
                }

                break;

            case CardSlotContent.StatusText:
                var statusText = presentation.StatusText;
                if (presentation.HasDualBuckets && !this._preferences.ShowDualQuotaBars)
                {
                    statusText = MainWindowRuntimeLogic.BuildSingleDualQuotaStatusText(
                        presentation, showUsed, this._preferences.DualQuotaSingleBarMode);
                }

                Brush statusBrush = presentation.StatusTone switch
                {
                    ProviderCardStatusTone.Missing => Brushes.IndianRed,
                    ProviderCardStatusTone.Warning => Brushes.Orange,
                    ProviderCardStatusTone.Error => Brushes.Red,
                    _ => this._getResourceBrush("SecondaryText", Brushes.Gray),
                };
                this.AddSlotText(panel, statusText, statusBrush, 10);
                break;

            case CardSlotContent.AccountName:
                if (!string.IsNullOrWhiteSpace(usage.AccountName))
                {
                    var name = this._isPrivacyMode ? "****" : usage.AccountName;
                    this.AddSlotText(panel, name, this._getResourceBrush("TertiaryText", Brushes.Gray), 9);
                }

                break;

            case CardSlotContent.AuthSource:
                if (!string.IsNullOrWhiteSpace(usage.AuthSource))
                {
                    this.AddSlotText(panel, usage.AuthSource, this._getResourceBrush("TertiaryText", Brushes.Gray), 9);
                }

                break;
        }
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

    private (double UsedPercent, double ColorPercent) GetSingleBarDualQuotaPercents(ProviderCardPresentation presentation)
    {
        var usePrimary = MainWindowRuntimeLogic.ShouldUsePrimaryDualBucket(
            presentation,
            this._preferences.DualQuotaSingleBarMode);
        var used = usePrimary
            ? presentation.DualBucketPrimaryUsed!.Value
            : presentation.DualBucketSecondaryUsed!.Value;
        var color = usePrimary
            ? presentation.DualBucketPrimaryColorPercent ?? used
            : presentation.DualBucketSecondaryColorPercent ?? used;
        return (used, color);
    }

    private Border CreateBulletMarker()
    {
        return new Border
        {
            Width = 4,
            Height = 4,
            Background = this._getResourceBrush("SecondaryText", Brushes.Gray),
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
            ? this._getResourceBrush("TertiaryText", Brushes.Gray)
            : this._getResourceBrush("PrimaryText", Brushes.White);
        var secondaryTextBrush = this._getResourceBrush("SecondaryText", Brushes.Gray);

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
