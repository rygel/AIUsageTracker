namespace AIUsageTracker.Core.Models;

public enum AppTheme
{
    Dark,
    Light,
    Corporate,
    Midnight,
    Dracula,
    Nord,
    Monokai,
    OneDark,
    SolarizedDark,
    SolarizedLight,
    CatppuccinFrappe,
    CatppuccinMacchiato,
    CatppuccinMocha,
    CatppuccinLatte
}

public class AppPreferences
{
    public bool ShowAll { get; set; } = false;
    public double WindowWidth { get; set; } = 420;
    public double WindowHeight { get; set; } = 500;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool StayOpen { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool AggressiveAlwaysOnTop { get; set; } = false;
    public bool ForceWin32Topmost { get; set; } = false;
    public bool CompactMode { get; set; } = true;
    public int ColorThresholdYellow { get; set; } = 60;
    public int ColorThresholdRed { get; set; } = 80;
    public bool InvertProgressBar { get; set; } = true;
    public bool InvertCalculations { get; set; } = false;
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 12;
    public bool FontBold { get; set; } = false;
    public bool FontItalic { get; set; } = false;
    public int AutoRefreshInterval { get; set; } = 300; // In seconds, 0 = Disabled
    public bool IsPrivacyMode { get; set; } = false;
    public bool EnableNotifications { get; set; } = false; // Global notification switch - disabled by default
    public double NotificationThreshold { get; set; } = 90.0; // Notify when usage exceeds this %
    public bool NotifyOnUsageThreshold { get; set; } = true;
    public bool NotifyOnQuotaExceeded { get; set; } = true;
    public bool NotifyOnProviderErrors { get; set; } = false;
    public bool EnableQuietHours { get; set; } = false;
    public string QuietHoursStart { get; set; } = "22:00";
    public string QuietHoursEnd { get; set; } = "07:00";
    public bool StartWithWindows { get; set; } = false;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public bool DebugMode { get; set; } = false; // Enable detailed debug logging
    
    // Collapsible section states
    public bool IsPlansAndQuotasCollapsed { get; set; } = false;
    public bool IsPayAsYouGoCollapsed { get; set; } = false;
    public bool IsAntigravityCollapsed { get; set; } = false;
    
    // Update channel (Stable or Beta)
    public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Stable;
}


