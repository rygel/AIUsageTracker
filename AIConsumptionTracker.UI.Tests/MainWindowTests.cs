using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;
using System.Diagnostics;

namespace AIConsumptionTracker.UI.Tests;

public class MainWindowTests
{
    private readonly string _appPath;

    public MainWindowTests()
    {
        // Path to the built UI executable
        // Assuming we build in Debug mode
        _appPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "AIConsumptionTracker.UI", "bin", "Debug", "net10.0-windows", "AIConsumptionTracker.UI.exe");
    }

    [Fact]
    public void MainWindow_ShouldLoadWithCorrectTitle()
    {
        if (!File.Exists(_appPath)) return;

        var app = Application.Launch(_appPath);
        try
        {
            using (var automation = new UIA3Automation())
            {
                var window = app.GetMainWindow(automation);
                Assert.Contains("AI Consumption", window.Title);
                
                // Use ByName which maps to the Label/Text of buttons/checkboxes
                var showAllToggle = window.FindFirstDescendant(cf => cf.ByName("Show All"));
                Assert.NotNull(showAllToggle);

                // Find by ToolTip or specific sequence
                var refreshBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("RefreshBtn"));
                if (refreshBtn == null) 
                    refreshBtn = window.FindFirstDescendant(cf => cf.ByHelpText("Refresh Data"));
                
                Assert.NotNull(refreshBtn);
            }
        }
        finally
        {
            app.Close();
        }
    }

    [Fact]
    public void SettingsWindow_ShouldOpenAndClose()
    {
        if (!File.Exists(_appPath)) return;

        var app = Application.Launch(_appPath);
        try
        {
            using (var automation = new UIA3Automation())
            {
                var window = app.GetMainWindow(automation);
                
                // Find Settings button by ToolTip (HelpText)
                var settingsBtn = window.FindFirstDescendant(cf => cf.ByHelpText("Provider Settings")).AsButton();
                settingsBtn.Invoke(); 
                
                // Wait for window with title "Settings"
                var settingsWindow = app.GetAllTopLevelWindows(automation).FirstOrDefault(w => w.Title == "Settings");
                
                Assert.NotNull(settingsWindow);
                var cancelBtn = settingsWindow.FindFirstDescendant(cf => cf.ByName("Cancel")).AsButton();
                Assert.NotNull(cancelBtn);
                cancelBtn.Invoke(); 
            }
        }
        finally
        {
            app.Close();
        }
    }

    [Fact]
    public void MainWindow_ToggleShowAll_ShouldUpdateStatus()
    {
        if (!File.Exists(_appPath)) return;

        var app = Application.Launch(_appPath);
        try
        {
            using (var automation = new UIA3Automation())
            {
                var window = app.GetMainWindow(automation);
                var showAllToggle = window.FindFirstDescendant(cf => cf.ByName("Show All")).AsCheckBox();
                
                bool initialState = showAllToggle.IsChecked ?? false;
                showAllToggle.IsChecked = !initialState;
                Assert.NotEqual(initialState, showAllToggle.IsChecked);
            }
        }
        finally
        {
            app.Close();
        }
    }
}
