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
        var presentation = MainWindowRuntimeLogic.Create(usage, showUsed);

        var grid = new Grid
        {
            Margin = new Thickness(isChild ? 20 : 0, 0, 0, 2),
            Height = 24,
            Background = Brushes.Transparent,
            Tag = providerId,
        };

        var pGrid = new Grid();
        if (presentation.HasDualBuckets)
        {
            pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            pGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var primaryRow = this.CreateProgressLayer(presentation.DualBucketPrimaryUsed!.Value, showUsed, opacity: 0.55);
            var secondaryRow = this.CreateProgressLayer(presentation.DualBucketSecondaryUsed!.Value, showUsed, opacity: 0.35);
            Grid.SetRow(primaryRow, 0);
            Grid.SetRow(secondaryRow, 1);
            pGrid.Children.Add(primaryRow);
            pGrid.Children.Add(secondaryRow);
        }
        else
        {
            var indicatorWidth = showUsed ? presentation.UsedPercent : presentation.RemainingPercent;
            var colorIndicatorPercent = GetColorIndicatorPercent(
                usage,
                presentation.UsedPercent,
                this._preferences.EnablePaceAdjustment);
            pGrid = this.CreateSingleProgressLayer(colorIndicatorPercent, indicatorWidth, opacity: 0.45);
        }

        pGrid.Visibility = presentation.ShouldHaveProgress ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(pGrid);

        var bg = new Border
        {
            Background = this._getResourceBrush("CardBackground", Brushes.DarkGray),
            CornerRadius = new CornerRadius(0),
            Visibility = presentation.ShouldHaveProgress ? Visibility.Collapsed : Visibility.Visible,
        };
        grid.Children.Add(bg);

        var contentPanel = new DockPanel { LastChildFill = false, Margin = new Thickness(6, 0, 6, 0) };
        if (isChild)
        {
            AddDockedElement(contentPanel, this.CreateBulletMarker(), Dock.Left);
        }
        else
        {
            var providerIcon = this._createProviderIcon(providerId);
            providerIcon.Margin = new Thickness(0, 0, 6, 0);
            providerIcon.Width = 14;
            providerIcon.Height = 14;
            providerIcon.VerticalAlignment = VerticalAlignment.Center;
            AddDockedElement(contentPanel, providerIcon, Dock.Left);
        }

        var statusText = presentation.StatusText;
        Brush statusBrush = presentation.StatusTone switch
        {
            ProviderCardStatusTone.Missing => Brushes.IndianRed,
            ProviderCardStatusTone.Warning => Brushes.Orange,
            ProviderCardStatusTone.Error => Brushes.Red,
            _ => this._getResourceBrush("SecondaryText", Brushes.Gray),
        };

        var resetBadgeText = this.BuildResetBadgeText(usage, presentation);
        if (!string.IsNullOrWhiteSpace(resetBadgeText))
        {
            AddDockedElement(
                contentPanel,
                this.CreateDockedTextBlock(
                    resetBadgeText,
                    fontSize: 10,
                    foreground: this._getResourceBrush("StatusTextWarning", Brushes.Goldenrod),
                    fontWeight: FontWeights.SemiBold,
                    margin: new Thickness(10, 0, 0, 0)),
                Dock.Right);
        }

        var paceBadgeText = GetPaceBadgeText(
            usage,
            presentation.UsedPercent,
            this._preferences.EnablePaceAdjustment);
        if (!string.IsNullOrWhiteSpace(paceBadgeText))
        {
            AddDockedElement(
                contentPanel,
                this.CreateDockedTextBlock(
                    paceBadgeText,
                    fontSize: 9,
                    foreground: this._getResourceBrush("ProgressBarGreen", Brushes.MediumSeaGreen),
                    fontWeight: FontWeights.SemiBold,
                    margin: new Thickness(6, 0, 0, 0)),
                Dock.Right);
        }

        if (this._preferences.ShowUsagePerHour && usage.UsagePerHour.HasValue)
        {
            AddDockedElement(
                contentPanel,
                this.CreateDockedTextBlock(
                    $"{usage.UsagePerHour.Value:F1}/hr",
                    fontSize: 9,
                    foreground: this._getResourceBrush("TertiaryText", Brushes.Gray),
                    margin: new Thickness(6, 0, 0, 0)),
                Dock.Right);
        }

        AddDockedElement(
            contentPanel,
            this.CreateDockedTextBlock(
                statusText,
                fontSize: 10,
                foreground: statusBrush,
                margin: new Thickness(10, 0, 0, 0)),
            Dock.Right);

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

        grid.Children.Add(contentPanel);

        if (presentation.IsStale)
        {
            grid.Opacity = 0.65;
        }

        var toolTipContent = BuildTooltipContent(usage, friendlyName);
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
        var resetTimes = ResolveResetTimes(
            usage,
            presentation.SuppressSingleResetTime);
        if (resetTimes.Count == 0)
        {
            return null;
        }

        var resetParts = resetTimes
            .Select(this._getRelativeTimeString)
            .ToList();
        return $"({string.Join(" | ", resetParts)})";
    }

    private static IReadOnlyList<DateTime> ResolveResetTimes(ProviderUsage usage, bool suppressSingleResetFallback)
    {
        ArgumentNullException.ThrowIfNull(usage);

        var detailResetTimes = usage.Details?
            .Where(detail => detail.DetailType == ProviderUsageDetailType.QuotaWindow)
            .Where(detail => detail.QuotaBucketKind != WindowKind.None)
            .Where(detail => detail.NextResetTime.HasValue)
            .Where(detail => UsageMath.GetEffectiveUsedPercent(detail).HasValue)
            .Select(detail => detail.NextResetTime!.Value)
            .Distinct()
            .ToList()
            ?? new List<DateTime>();

        if (detailResetTimes.Count >= 2)
        {
            return detailResetTimes;
        }

        if (suppressSingleResetFallback)
        {
            return Array.Empty<DateTime>();
        }

        return usage.NextResetTime.HasValue
            ? new[] { usage.NextResetTime.Value }
            : Array.Empty<DateTime>();
    }

    private static double GetColorIndicatorPercent(
        ProviderUsage usage,
        double usedPercent,
        bool enablePaceAdjustment,
        DateTime? nowUtc = null)
    {
        if (!enablePaceAdjustment || !usage.PeriodDuration.HasValue || !usage.NextResetTime.HasValue)
        {
            return usedPercent;
        }

        return UsageMath.CalculatePaceAdjustedColorPercent(
            usedPercent,
            usage.NextResetTime.Value.ToUniversalTime(),
            usage.PeriodDuration.Value,
            nowUtc);
    }

    private static string? GetPaceBadgeText(
        ProviderUsage usage,
        double usedPercent,
        bool enablePaceAdjustment,
        DateTime? nowUtc = null)
    {
        if (!enablePaceAdjustment || !usage.PeriodDuration.HasValue || !usage.NextResetTime.HasValue)
        {
            return null;
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var period = usage.PeriodDuration.Value;
        var periodStart = usage.NextResetTime.Value.ToUniversalTime() - period;
        var elapsed = now - periodStart;
        var elapsedFraction = Math.Clamp(elapsed.TotalSeconds / period.TotalSeconds, 0.01, 1.0);
        var expectedPercent = elapsedFraction * 100.0;

        return usedPercent < expectedPercent * 0.95 ? "On pace" : null;
    }

    private static string? BuildTooltipContent(ProviderUsage usage, string friendlyName)
    {
        var tooltipBuilder = new System.Text.StringBuilder();
        tooltipBuilder.AppendLine(friendlyName);
        tooltipBuilder.AppendLine($"Status: {(usage.IsAvailable ? "Active" : "Inactive")}");
        if (!string.IsNullOrEmpty(usage.Description))
        {
            tooltipBuilder.AppendLine($"Description: {usage.Description}");
        }

        if (usage.Details?.Any() == true)
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine("Rate Limits:");
            foreach (var detail in usage.Details
                         .OrderBy(GetDetailSortOrder)
                         .ThenBy(GetDetailDisplayName, StringComparer.OrdinalIgnoreCase))
            {
                var detailValue = GetDetailDisplayValue(detail);
                if (string.IsNullOrWhiteSpace(detailValue))
                {
                    continue;
                }

                tooltipBuilder.AppendLine($"  {GetDetailDisplayName(detail)}: {detailValue}");
            }
        }

        if (!string.IsNullOrEmpty(usage.AuthSource))
        {
            tooltipBuilder.AppendLine();
            tooltipBuilder.AppendLine($"Source: {usage.AuthSource}");
        }

        var result = tooltipBuilder.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static string GetDetailDisplayName(ProviderUsageDetail detail)
    {
        return detail.Name;
    }

    private static string GetDetailDisplayValue(ProviderUsageDetail detail)
    {
        return MainWindowRuntimeLogic.GetStoredDisplayText(detail);
    }

    private static int GetDetailSortOrder(ProviderUsageDetail detail)
    {
        return (detail.DetailType, detail.QuotaBucketKind) switch
        {
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Burst) => 0,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.Rolling) => 1,
            (ProviderUsageDetailType.QuotaWindow, WindowKind.ModelSpecific) => 2,
            (ProviderUsageDetailType.QuotaWindow, _) => 3,
            (ProviderUsageDetailType.Model, _) => 3,
            (ProviderUsageDetailType.Credit, _) => 4,
            _ => 5,
        };
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


