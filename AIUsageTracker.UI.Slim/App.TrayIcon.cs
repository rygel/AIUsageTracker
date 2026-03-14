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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class App
{
    public void UpdateProviderTrayIcons(
        IReadOnlyList<ProviderUsage> usages,
        IReadOnlyList<ProviderConfig> configs,
        AppPreferences? prefs = null)
    {
        var displayPreferences = Host.Services.GetRequiredService<DisplayPreferencesService>();
        var yellowThreshold = prefs?.ColorThresholdYellow ?? 60;
        var redThreshold = prefs?.ColorThresholdRed ?? 80;
        var showUsed = prefs != null && displayPreferences.ShouldShowUsedPercentages(prefs);
        var desiredIcons = this.BuildDesiredIcons(usages, configs, showUsed);

        this.SyncProviderTrayIcons(desiredIcons, yellowThreshold, redThreshold, showUsed);
    }

    private Dictionary<string, (string ToolTip, double Percentage, bool IsQuota)> BuildDesiredIcons(
        IReadOnlyList<ProviderUsage> usages,
        IReadOnlyList<ProviderConfig> configs,
        bool showUsed)
    {
        var desiredIcons = new Dictionary<string, (string ToolTip, double Percentage, bool IsQuota)>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in configs)
        {
            var usage = usages.FirstOrDefault(u => u.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (usage == null)
            {
                continue;
            }

            if (config.ShowInTray &&
                usage.IsAvailable &&
                !usage.Description.Contains("unknown", StringComparison.OrdinalIgnoreCase))
            {
                var isQuota = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                var statusText = ProviderCardPresentationCatalog.Create(usage, showUsed).StatusText;
                var providerLabel = ProviderMetadataCatalog.ResolveDisplayLabel(usage);
                desiredIcons[config.ProviderId] = ($"{providerLabel}: {statusText}", usage.RemainingPercent, isQuota);
            }

            if (config.EnabledSubTrays == null || usage.Details == null)
            {
                continue;
            }

            foreach (var subName in config.EnabledSubTrays)
            {
                var detail = usage.Details.FirstOrDefault(d => d.Name.Equals(subName, StringComparison.OrdinalIgnoreCase));
                if (detail == null || !this.IsSubTrayEligibleDetail(detail))
                {
                    continue;
                }

                var isQuotaSub = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                var detailPresentation = ProviderSubDetailPresentationCatalog.Create(
                    detail,
                    isQuotaSub,
                    showUsed,
                    _ => string.Empty);
                if (!detailPresentation.HasProgress)
                {
                    continue;
                }

                var key = $"{config.ProviderId}:{subName}";
                var providerLabel = ProviderMetadataCatalog.ResolveDisplayLabel(usage);
                desiredIcons[key] = (
                    $"{providerLabel} - {subName}: {detailPresentation.DisplayText}",
                    showUsed ? detailPresentation.UsedPercent : detailPresentation.IndicatorWidth,
                    isQuotaSub);
            }
        }

        return desiredIcons;
    }

    private void SyncProviderTrayIcons(
        IReadOnlyDictionary<string, (string ToolTip, double Percentage, bool IsQuota)> desiredIcons,
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
            var iconSource = this.GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, showUsed, info.IsQuota);

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

    private string ResolveTrayIconPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "AIUsageTracker.UI.Slim", "Assets", "app_icon.ico"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private bool IsSubTrayEligibleDetail(ProviderUsageDetail detail)
    {
        return detail.IsDisplayableSubProviderDetail();
    }

    private ImageSource GenerateUsageIcon(
        double percentage,
        int yellowThreshold,
        int redThreshold,
        bool showUsed = false,
        bool isQuota = false)
    {
        var size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 20, 20)), null, new Rect(0, 0, size, size));
            dc.DrawRectangle(null, new Pen(Brushes.DimGray, 1), new Rect(0.5, 0.5, size - 1, size - 1));

            var fillBrush = isQuota
                ? (percentage < (100 - redThreshold)
                    ? Brushes.Crimson
                    : (percentage < (100 - yellowThreshold) ? Brushes.Gold : Brushes.MediumSeaGreen))
                : (percentage > redThreshold
                    ? Brushes.Crimson
                    : (percentage > yellowThreshold ? Brushes.Gold : Brushes.MediumSeaGreen));

            var barWidth = size - 6;
            var barHeight = size - 6;
            double fillHeight;
            if (showUsed)
            {
                var remaining = Math.Max(0, 100.0 - percentage);
                fillHeight = (remaining / 100.0) * barHeight;
            }
            else
            {
                fillHeight = (percentage / 100.0) * barHeight;
            }

            dc.DrawRectangle(fillBrush, null, new Rect(3, size - 3 - fillHeight, barWidth, fillHeight));
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

        var trayIconPath = this.ResolveTrayIconPath();
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
