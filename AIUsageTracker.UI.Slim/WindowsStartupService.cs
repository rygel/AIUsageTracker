// <copyright file="WindowsStartupService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;

using Microsoft.Win32;

namespace AIUsageTracker.UI.Slim;

internal static class WindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string UiValueName = "AI Usage Tracker";

    public static bool IsUiStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(UiValueName) != null;
    }

    public static void Apply(bool startUi)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key == null)
        {
            return;
        }

        var exePath = Path.Combine(AppContext.BaseDirectory, "AIUsageTracker.exe");
        Apply(startUi, key, exePath);
    }

    internal static void Apply(bool startUi, RegistryKey key, string exePath)
    {
        if (startUi && File.Exists(exePath))
        {
            var command = $"\"{exePath}\"";
            var existing = key.GetValue(UiValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            if (existing != null &&
                key.GetValueKind(UiValueName) == RegistryValueKind.ExpandString &&
                string.Equals(Environment.ExpandEnvironmentVariables(existing), command, StringComparison.OrdinalIgnoreCase))
            {
                // Keep the owner's portable registration only while it targets this installation.
                return;
            }

            key.SetValue(UiValueName, command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(UiValueName, throwOnMissingValue: false);
        }
    }
}
