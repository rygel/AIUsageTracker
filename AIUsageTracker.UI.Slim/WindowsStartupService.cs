// <copyright file="WindowsStartupService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;

using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

internal static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string MonitorValueName = "AI Usage Tracker Monitor";
    private const string UiValueName = "AI Usage Tracker";

    public static (bool MonitorEnabled, bool UiEnabled) Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        if (key == null)
        {
            return (false, false);
        }

        return (
            key.GetValue(MonitorValueName) != null,
            key.GetValue(UiValueName) != null);
    }

    public static void Apply(bool startMonitor, bool startUi)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null)
        {
            return;
        }

        ApplyEntry(key, MonitorValueName, startMonitor, GetMonitorExePath());
        ApplyEntry(key, UiValueName, startUi, GetUiExePath());
    }

    private static void ApplyEntry(RegistryKey key, string valueName, bool enable, string exePath)
    {
        if (enable && File.Exists(exePath))
        {
            key.SetValue(valueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }

    private static string GetMonitorExePath() =>
        Path.Combine(AppContext.BaseDirectory, "AIUsageTracker.Monitor.exe");

    private static string GetUiExePath() =>
        Path.Combine(AppContext.BaseDirectory, "AIUsageTracker.exe");
}
