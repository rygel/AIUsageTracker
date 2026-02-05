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
    public bool InvertProgressBar { get; set; } = false;
}

