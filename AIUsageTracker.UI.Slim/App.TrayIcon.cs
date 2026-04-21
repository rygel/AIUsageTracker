// <copyright file="App.TrayIcon.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using CommunityToolkit.Mvvm.Input;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class App
{
    public void UpdateProviderTrayIcons(
        IReadOnlyList<ProviderUsage> usages,
        IReadOnlyList<ProviderConfig> configs,
        AppPreferences? prefs = null)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var yellowThreshold = prefs?.ColorThresholdYellow ?? 60;
        var redThreshold = prefs?.ColorThresholdRed ?? 80;
        var enablePaceAdjustment = prefs?.EnablePaceAdjustment ?? true;
        var showUsed = prefs?.ShowUsedPercentages ?? false;
        var showDualQuotaBars = prefs?.ShowDualQuotaBars ?? true;
        var dualQuotaSingleBarMode = prefs?.DualQuotaSingleBarMode ?? DualQuotaSingleBarMode.Rolling;
        var desiredIcons = BuildDesiredIcons(
            usages,
            configs,
            showUsed,
            showDualQuotaBars,
            dualQuotaSingleBarMode,
            enablePaceAdjustment);

        this.SyncProviderTrayIcons(desiredIcons, yellowThreshold, redThreshold, showUsed);
    }

    private static Dictionary<string, (string ToolTip, double FillPercent, PaceColorResult PaceColor, bool IsQuota)> BuildDesiredIcons(
        IReadOnlyList<ProviderUsage> usages,
        IReadOnlyList<ProviderConfig> configs,
        bool showUsed,
        bool showDualQuotaBars,
        DualQuotaSingleBarMode dualQuotaSingleBarMode,
        bool enablePaceAdjustment)
    {
        var desiredIcons = new Dictionary<string, (string ToolTip, double FillPercent, PaceColorResult PaceColor, bool IsQuota)>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            var configDefinition = ProviderMetadataCatalog.Find(config.ProviderId);
            var usage = usages
                .Where(u =>
                {
                    var usageProviderId = u.ProviderId ?? string.Empty;
                    return configDefinition?.HandlesProviderId(usageProviderId) ??
                           string.Equals(usageProviderId, config.ProviderId, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(u => u.UsedPercent)
                .FirstOrDefault();
            if (usage == null)
            {
                continue;
            }

            if (config.ShowInTray &&
                usage.IsAvailable &&
                !usage.Description.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                var isQuota = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                var presentation = MainWindowRuntimeLogic.Create(usage, showUsed, enablePaceAdjustment);
                var statusText = presentation.StatusText;
                if (presentation.HasDualBuckets && !showDualQuotaBars)
                {
                    statusText = MainWindowRuntimeLogic.BuildSingleDualQuotaStatusText(
                        presentation,
                        showUsed,
                        dualQuotaSingleBarMode);
                }

                var providerLabel = usage.ProviderName ?? ProviderMetadataCatalog.GetConfiguredDisplayName(usage.ProviderId ?? string.Empty);
                var paceColor = UsageMath.ComputePaceColor(
                    usage.UsedPercent,
                    usage.NextResetTime,
                    usage.PeriodDuration,
                    enablePaceAdjustment);
                desiredIcons[config.ProviderId] = ($"{providerLabel}: {statusText}", usage.RemainingPercent, paceColor, isQuota);
            }
        }

        return desiredIcons;
    }

    private void SyncProviderTrayIcons(
        Dictionary<string, (string ToolTip, double FillPercent, PaceColorResult PaceColor, bool IsQuota)> desiredIcons,
        int yellowThreshold,
        int redThreshold,
        bool showUsed)
    {
        var currentKeys = this._providerTrayIcons.Keys.ToList();
        foreach (var key in currentKeys)
        {
            if (desiredIcons.ContainsKey(key))
            {
                continue;
            }

            this._providerTrayIcons[key].Dispose();
            this._providerTrayIcons.Remove(key);
        }

        foreach (var kvp in desiredIcons)
        {
            var key = kvp.Key;
            var info = kvp.Value;
            var iconSource = GenerateUsageIcon(info.FillPercent, info.PaceColor, yellowThreshold, redThreshold, showUsed);

            if (!this._providerTrayIcons.ContainsKey(key))
            {
                var tray = new TaskbarIcon
                {
                    ToolTipText = info.ToolTip,
                    IconSource = iconSource,
                };
                tray.TrayLeftMouseDown += (_, _) => this.ShowMainWindow();
                tray.TrayMouseDoubleClick += (_, _) => this.ShowMainWindow();
                this._providerTrayIcons.Add(key, tray);
            }
            else
            {
                var tray = this._providerTrayIcons[key];
                tray.ToolTipText = info.ToolTip;
                tray.IconSource = iconSource;
            }
        }
    }

    private static string ResolveTrayIconPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "AIUsageTracker.UI.Slim", "Assets", "app_icon.ico"),
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static RenderTargetBitmap GenerateUsageIcon(
        double fillPercent,
        PaceColorResult paceColor,
        int yellowThreshold,
        int redThreshold,
        bool showUsed = false)
    {
        var size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(brush: new SolidColorBrush(Color.FromRgb(20, 20, 20)), pen: null, rectangle: new Rect(0, 0, size, size));
            dc.DrawRectangle(brush: null, pen: new Pen(Brushes.DimGray, 1), rectangle: new Rect(0.5, 0.5, size - 1, size - 1));

            Brush fillBrush;
            if (paceColor.IsPaceAdjusted)
            {
                // Tier is the single source of truth for pace-adjusted icons.
                fillBrush = paceColor.PaceTier == PaceTier.OverPace ? Brushes.Crimson : Brushes.MediumSeaGreen;
            }
            else
            {
                var colorPercent = paceColor.ColorPercent;
                if (colorPercent >= redThreshold)
                {
                    fillBrush = Brushes.Crimson;
                }
                else if (colorPercent >= yellowThreshold)
                {
                    fillBrush = Brushes.Gold;
                }
                else
                {
                    fillBrush = Brushes.MediumSeaGreen;
                }
            }

            var barWidth = size - 6;
            var barHeight = size - 6;
            double fillHeight;
            if (showUsed)
            {
                var remaining = Math.Max(0, 100.0 - fillPercent);
                fillHeight = (remaining / 100.0) * barHeight;
            }
            else
            {
                fillHeight = (fillPercent / 100.0) * barHeight;
            }

            dc.DrawRectangle(brush: fillBrush, pen: null, rectangle: new Rect(3, size - 3 - fillHeight, barWidth, fillHeight));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private void InitializeTrayIcon()
    {
        var contextMenu = new ContextMenu();

        var showMenuItem = new MenuItem { Header = "Show" };
        showMenuItem.Click += (_, _) => this.ShowMainWindow();
        contextMenu.Items.Add(showMenuItem);

        contextMenu.Items.Add(new Separator());

        var infoMenuItem = new MenuItem { Header = "Info" };
        infoMenuItem.Click += (_, _) => this.OpenInfoDialog();
        contextMenu.Items.Add(infoMenuItem);

        contextMenu.Items.Add(new Separator());

        var exitMenuItem = new MenuItem { Header = "Exit" };
        exitMenuItem.Click += (_, _) => this.Shutdown();
        contextMenu.Items.Add(exitMenuItem);

        var trayIconPath = App.ResolveTrayIconPath();
        var trayIcon = File.Exists(trayIconPath)
            ? new System.Drawing.Icon(trayIconPath)
            : System.Drawing.SystemIcons.Application;

        this._trayIcon = new TaskbarIcon
        {
            Icon = trayIcon,
            ToolTipText = "AI Usage Tracker",
            ContextMenu = contextMenu,
            DoubleClickCommand = new RelayCommand(this.ShowMainWindow),
        };

        if (!File.Exists(trayIconPath))
        {
            CreateLogger<App>().LogWarning(
                "Tray icon not found at expected paths. Falling back to system icon. Tried: {TrayIconPath}",
                trayIconPath);
        }
    }

    private void ShowMainWindow()
    {
        if (this._mainWindow == null)
        {
            return;
        }

        this._mainWindow.Show();
        this._mainWindow.Activate();
    }
}
