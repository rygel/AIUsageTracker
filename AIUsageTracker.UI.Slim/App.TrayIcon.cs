using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace AIUsageTracker.UI.Slim;

public partial class App
{
    private void InitializeTrayIcon()
    {
        // Create context menu
        var contextMenu = new ContextMenu();
        
        // Show menu item
        var showMenuItem = new MenuItem { Header = "Show" };
        showMenuItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(showMenuItem);
        
        // Separator
        contextMenu.Items.Add(new Separator());
        
        // Info menu item
        var infoMenuItem = new MenuItem { Header = "Info" };
        infoMenuItem.Click += (s, e) => OpenInfoDialog();
        contextMenu.Items.Add(infoMenuItem);
        
        // Separator
        contextMenu.Items.Add(new Separator());
        
        // Exit menu item
        var exitMenuItem = new MenuItem { Header = "Exit" };
        exitMenuItem.Click += (s, e) =>
        {
            Shutdown();
        };
        contextMenu.Items.Add(exitMenuItem);
        
        // Create tray icon
        var trayIconPath = ResolveTrayIconPath();
        var trayIcon = File.Exists(trayIconPath)
            ? new System.Drawing.Icon(trayIconPath)
            : System.Drawing.SystemIcons.Application;

        _trayIcon = new TaskbarIcon
        {
            Icon = trayIcon,
            ToolTipText = "AI Usage Tracker",
            ContextMenu = contextMenu,
            DoubleClickCommand = new RelayCommand(() =>
            {
                ShowMainWindow();
            })
        };

        if (!File.Exists(trayIconPath))
        {
            CreateLogger<App>().LogWarning("Tray icon not found at expected paths. Falling back to system icon. Tried: {TrayIconPath}", trayIconPath);
        }
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private static string ResolveTrayIconPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "app_icon.ico"),
            Path.Combine(Environment.CurrentDirectory, "AIUsageTracker.UI.Slim", "Assets", "app_icon.ico")
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

    public void UpdateProviderTrayIcons(List<ProviderUsage> usages, List<ProviderConfig> configs, AppPreferences? prefs = null)
    {
        var desiredIcons = new Dictionary<string, (string ToolTip, double Percentage, bool IsQuota)>(StringComparer.OrdinalIgnoreCase);
        var yellowThreshold = prefs?.ColorThresholdYellow ?? 60;
        var redThreshold = prefs?.ColorThresholdRed ?? 80;
        var invert = prefs?.InvertProgressBar ?? false;

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
                desiredIcons[config.ProviderId] = ($"{usage.ProviderName}: {usage.Description}", usage.RequestsPercentage, isQuota);
            }

            if (config.EnabledSubTrays == null || usage.Details == null)
            {
                continue;
            }

            foreach (var subName in config.EnabledSubTrays)
            {
                var detail = usage.Details.FirstOrDefault(d => d.Name.Equals(subName, StringComparison.OrdinalIgnoreCase));
                if (detail == null)
                {
                    continue;
                }

                if (!IsSubTrayEligibleDetail(detail))
                {
                    continue;
                }

                var detailPercent = ParsePercent(detail.Used);
                if (!detailPercent.HasValue)
                {
                    continue;
                }

                var key = $"{config.ProviderId}:{subName}";
                var isQuotaSub = usage.IsQuotaBased || usage.PlanType == PlanType.Coding;
                desiredIcons[key] = (
                    $"{usage.ProviderName} - {subName}: {detail.Description} ({detail.Used})",
                    detailPercent.Value,
                    isQuotaSub
                );
            }
        }

        var currentKeys = _providerTrayIcons.Keys.ToList();
        foreach (var key in currentKeys)
        {
            if (desiredIcons.ContainsKey(key))
            {
                continue;
            }

            _providerTrayIcons[key].Dispose();
            _providerTrayIcons.Remove(key);
        }

        foreach (var kvp in desiredIcons)
        {
            var key = kvp.Key;
            var info = kvp.Value;
            var iconSource = GenerateUsageIcon(info.Percentage, yellowThreshold, redThreshold, invert, info.IsQuota);

            if (!_providerTrayIcons.ContainsKey(key))
            {
                var tray = new TaskbarIcon
                {
                    ToolTipText = info.ToolTip,
                    IconSource = iconSource
                };
                tray.TrayLeftMouseDown += (s, e) => ShowMainWindow();
                tray.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
                _providerTrayIcons.Add(key, tray);
            }
            else
            {
                var tray = _providerTrayIcons[key];
                tray.ToolTipText = info.ToolTip;
                tray.IconSource = iconSource;
            }
        }
    }

    private static double? ParsePercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsedValue = value.Replace("%", string.Empty).Trim();
        return double.TryParse(parsedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, Math.Min(100, parsed))
            : null;
    }

    private static bool IsSubTrayEligibleDetail(ProviderUsageDetail detail)
    {
        if (string.IsNullOrWhiteSpace(detail.Name))
        {
            return false;
        }

        return !detail.Name.Contains("window", StringComparison.OrdinalIgnoreCase) &&
               !detail.Name.Contains("credit", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageSource GenerateUsageIcon(double percentage, int yellowThreshold, int redThreshold, bool invert = false, bool isQuota = false)
    {
        var size = 32;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 20, 20)), null, new Rect(0, 0, size, size));
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
            if (invert)
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
}

// Simple relay command implementation
public class RelayCommand : System.Windows.Input.ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add { System.Windows.Input.CommandManager.RequerySuggested += value; }
        remove { System.Windows.Input.CommandManager.RequerySuggested -= value; }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();
}
