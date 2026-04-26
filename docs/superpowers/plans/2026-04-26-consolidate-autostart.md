# Consolidate Auto-Start Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove redundant Monitor auto-start checkboxes and simplify to a single "Start UI with Windows" toggle.

**Architecture:** The UI always auto-starts the Monitor (`StartMonitorWarmup()` in `App.xaml.cs`). The only user-facing startup option is "Start UI with Windows" which controls the HKCU Run registry entry. Removing the dead Monitor tab checkbox and the redundant Layout tab Monitor startup checkbox eliminates confusion.

**Tech Stack:** WPF (XAML + code-behind), Windows Registry, Inno Setup

---

### Task 1: Remove dead `AutoStartMonitorCheck` from Monitor tab XAML

**Files:**
- Modify: `AIUsageTracker.UI.Slim/SettingsWindow.xaml:828-830`

- [ ] **Step 1: Remove the dead checkbox**

Remove lines 828-830 from `SettingsWindow.xaml`:

```xml
                                     <CheckBox x:Name="AutoStartMonitorCheck" Content="Auto-start Monitor"
                                               IsChecked="True" Margin="0,8,0,0"
                                               Foreground="{DynamicResource SecondaryText}"/>
```

- [ ] **Step 2: Build to verify no compile errors**

Run: `dotnet build AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj --configuration Debug`
Expected: Build succeeds (the checkbox was dead code with no C# references).

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.UI.Slim/SettingsWindow.xaml
git commit -m "refactor: remove dead AutoStartMonitorCheck from Monitor tab"
```

---

### Task 2: Remove "Start Monitor with Windows" from Layout tab XAML

**Files:**
- Modify: `AIUsageTracker.UI.Slim/SettingsWindow.xaml:529-535`

- [ ] **Step 1: Remove the Monitor startup checkbox**

Replace the "Windows Startup" section in `SettingsWindow.xaml` (lines 529-539). Remove `StartMonitorWithWindowsCheck` and keep only `StartUiWithWindowsCheck`. Change the section label from "Windows Startup" to "Startup":

Old (lines 529-539):
```xml
                        <!-- Windows Startup -->
                        <TextBlock Text="Windows Startup" FontWeight="Bold" FontSize="12"
                                   Foreground="{DynamicResource PrimaryText}" Margin="0,0,0,10"/>
                        <CheckBox x:Name="StartMonitorWithWindowsCheck" Content="Start Monitor with Windows"
                                  Margin="0,4" Foreground="{DynamicResource SecondaryText}"
                                  ToolTip="Automatically start the background Monitor service when Windows starts."
                                  Checked="LayoutSetting_Changed" Unchecked="LayoutSetting_Changed"/>
                        <CheckBox x:Name="StartUiWithWindowsCheck" Content="Start UI with Windows"
                                  Margin="0,4" Foreground="{DynamicResource SecondaryText}"
                                  ToolTip="Automatically start the UI when Windows starts."
                                  Checked="LayoutSetting_Changed" Unchecked="LayoutSetting_Changed"/>
```

New:
```xml
                        <!-- Startup -->
                        <TextBlock Text="Startup" FontWeight="Bold" FontSize="12"
                                   Foreground="{DynamicResource PrimaryText}" Margin="0,0,0,10"/>
                        <CheckBox x:Name="StartUiWithWindowsCheck" Content="Start with Windows"
                                  Margin="0,4" Foreground="{DynamicResource SecondaryText}"
                                  ToolTip="Automatically start the application when Windows starts. The Monitor will start automatically with the UI."
                                  Checked="LayoutSetting_Changed" Unchecked="LayoutSetting_Changed"/>
```

- [ ] **Step 2: Commit**

```bash
git add AIUsageTracker.UI.Slim/SettingsWindow.xaml
git commit -m "refactor: remove Start Monitor with Windows checkbox from Layout tab"
```

---

### Task 3: Simplify `WindowsStartupService.cs` to UI-only

**Files:**
- Modify: `AIUsageTracker.UI.Slim/WindowsStartupService.cs`

- [ ] **Step 1: Rewrite to handle only UI startup**

Replace the entire contents of `WindowsStartupService.cs` with:

```csharp
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

        if (startUi && File.Exists(exePath))
        {
            key.SetValue(UiValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(UiValueName, throwOnMissingValue: false);
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj --configuration Debug`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.UI.Slim/WindowsStartupService.cs
git commit -m "refactor: simplify WindowsStartupService to UI-only registry entry"
```

---

### Task 4: Update `SettingsWindow.xaml.cs` load/save logic

**Files:**
- Modify: `AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs`

- [ ] **Step 1: Update `PopulateLayoutSettings()` (line 478-480)**

Old:
```csharp
        var (monitorAutoStart, uiAutoStart) = WindowsStartupService.Read();
        this.StartMonitorWithWindowsCheck.IsChecked = monitorAutoStart;
        this.StartUiWithWindowsCheck.IsChecked = uiAutoStart;
```

New:
```csharp
        this.StartUiWithWindowsCheck.IsChecked = WindowsStartupService.IsUiStartupEnabled();
```

- [ ] **Step 2: Update `PersistAllSettingsAsync()` (lines 788-792)**

Old:
```csharp
            var startMonitor = this.StartMonitorWithWindowsCheck.IsChecked ?? false;
            var startUi = this.StartUiWithWindowsCheck.IsChecked ?? false;
            this._preferences.StartMonitorWithWindows = startMonitor;
            this._preferences.StartUiWithWindows = startUi;
            WindowsStartupService.Apply(startMonitor, startUi);
```

New:
```csharp
            var startUi = this.StartUiWithWindowsCheck.IsChecked ?? false;
            this._preferences.StartUiWithWindows = startUi;
            WindowsStartupService.Apply(startUi);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj --configuration Debug`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs
git commit -m "refactor: update SettingsWindow to use simplified WindowsStartupService"
```

---

### Task 5: Remove `StartMonitorWithWindows` from `AppPreferences`

**Files:**
- Modify: `AIUsageTracker.Core/Models/AppPreferences.cs:75`

- [ ] **Step 1: Remove the property**

Remove line 75:

```csharp
    public bool StartMonitorWithWindows { get; set; } = false;
```

- [ ] **Step 2: Build the entire solution to verify no references remain**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds (no remaining references to `StartMonitorWithWindows`).

- [ ] **Step 3: Commit**

```bash
git add AIUsageTracker.Core/Models/AppPreferences.cs
git commit -m "refactor: remove StartMonitorWithWindows from AppPreferences"
```

---

### Task 6: Update installer to remove Monitor startup task

**Files:**
- Modify: `scripts/setup.iss:228-253`

- [ ] **Step 1: Remove `startupmonitor` task (line 230)**

Remove:
```
Name: "startupmonitor"; Description: "Run AI Usage Tracker Monitor at Windows Startup"; GroupDescription: "Additional options:"; Flags: unchecked; Components: apps\monitor
```

- [ ] **Step 2: Remove Monitor registry entry (line 252)**

Remove:
```
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AI Usage Tracker Monitor"; ValueData: """{app}\AIUsageTracker.Monitor.exe"""; Tasks: startupmonitor; Flags: uninsdeletevalue
```

- [ ] **Step 3: Commit**

```bash
git add scripts/setup.iss
git commit -m "refactor: remove Monitor startup task from installer"
```

---

### Task 7: Run full validation

**Files:** None (verification only)

- [ ] **Step 1: Build the entire solution**

Run: `dotnet build AIUsageTracker.sln --configuration Debug`
Expected: Build succeeds with no errors.

- [ ] **Step 2: Run all tests**

Run: `dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug --no-build --verbosity normal`
Expected: All tests pass.

- [ ] **Step 3: Run format check**

Run: `dotnet format --verify-no-changes --severity warn`
Expected: No format errors (warnings OK).
