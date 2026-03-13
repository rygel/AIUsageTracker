// <copyright file="AppPreferences.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageTracker.Core.Models;

public class AppPreferences
{
    public const int CurrentSchemaVersion = 2;

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

    [JsonConverter(typeof(JsonStringEnumConverter<PercentageDisplayMode>))]
    public PercentageDisplayMode PercentageDisplayMode { get; set; } = PercentageDisplayMode.Remaining;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    [JsonIgnore]
    public bool ShowUsedPercentages
    {
        get => this.PercentageDisplayMode == PercentageDisplayMode.Used;
        set => this.PercentageDisplayMode = value ? PercentageDisplayMode.Used : PercentageDisplayMode.Remaining;
    }

    public string FontFamily { get; set; } = "Segoe UI";

    public int FontSize { get; set; } = 12;

    public bool FontBold { get; set; } = false;

    public bool FontItalic { get; set; } = false;

    public int AutoRefreshInterval { get; set; } = 300; // In seconds, 0 = Disabled

    // Global cap for concurrent provider API requests across all providers.
    public int MaxConcurrentProviderRequests { get; set; } = 6;

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

    public static AppPreferences Deserialize(string json)
    {
        var preferences = JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
        preferences.ApplyMigrations(json);
        preferences.SchemaVersion = CurrentSchemaVersion;
        return preferences;
    }

    private void ApplyMigrations(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!TryGetProperty(document.RootElement, nameof(this.SchemaVersion), out _) ||
            this.SchemaVersion < CurrentSchemaVersion)
        {
            this.ApplyLegacyDisplayModeCompatibility(document.RootElement);
        }
    }

    private void ApplyLegacyDisplayModeCompatibility(JsonElement root)
    {
        if (TryGetProperty(root, nameof(this.PercentageDisplayMode), out _))
        {
            return;
        }

        if (TryGetBooleanProperty(root, nameof(this.ShowUsedPercentages), out var showUsed) ||
            TryGetBooleanProperty(root, "InvertCalculations", out showUsed) ||
            TryGetBooleanProperty(root, "InvertProgressBar", out showUsed))
        {
            this.ShowUsedPercentages = showUsed;
        }
    }

    private static bool TryGetBooleanProperty(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!TryGetProperty(element, propertyName, out var property) || (property.ValueKind != JsonValueKind.True && property.ValueKind != JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement property)
    {
        foreach (var candidate in element.EnumerateObject())
        {
            if (string.Equals(candidate.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }
}
