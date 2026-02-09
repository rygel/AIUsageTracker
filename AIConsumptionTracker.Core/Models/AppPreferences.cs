namespace AIConsumptionTracker.Core.Models;

public class AppPreferences
{
    public bool ShowAll { get; set; } = false;
    public double WindowWidth { get; set; } = 420;
    public double WindowHeight { get; set; } = 500;
    public bool StayOpen { get; set; } = false;
    public bool AlwaysOnTop { get; set; } = true;
    public bool CompactMode { get; set; } = true;
    public int ColorThresholdYellow { get; set; } = 60;
    public int ColorThresholdRed { get; set; } = 80;
    public bool InvertProgressBar { get; set; } = true;
    public string FontFamily { get; set; } = "Segoe UI";
    public int FontSize { get; set; } = 12;
    public bool FontBold { get; set; } = false;
    public bool FontItalic { get; set; } = false;
    public int AutoRefreshInterval { get; set; } = 300; // In seconds, 0 = Disabled
    public bool IsPrivacyMode { get; set; } = false;
    public bool EnableNotifications { get; set; } = true; // Global notification switch
}

