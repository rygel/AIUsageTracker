using FlaUI.Core;
using FlaUI.UIA3;
using FlaUI.Core.AutomationElements;
using System.IO;
using System.Diagnostics;

namespace AIConsumptionTracker.UI.Tests;

public class AppFixture : IDisposable
{
    public Application App { get; private set; }
    public UIA3Automation Automation { get; private set; }
    public Window MainWindow { get; private set; }

    public AppFixture()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string appPath = Path.Combine(baseDir, "..", "..", "..", "..", "AIConsumptionTracker.UI", "bin", "Debug", "net10.0-windows", "AIConsumptionTracker.UI.exe");

        if (!File.Exists(appPath))
        {
            throw new FileNotFoundException($"App not found at {appPath}");
        }

        App = Application.Launch(appPath);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation);
    }

    public void Dispose()
    {
        Automation?.Dispose();
        App?.Close();
    }
}

public class MainWindowTests : IClassFixture<AppFixture>
{
    private readonly AppFixture _fixture;

    public MainWindowTests(AppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void MainWindow_ShouldLoadWithCorrectTitle()
    {
        Assert.Contains("AI Consumption", _fixture.MainWindow.Title);
    }

    [Fact]
    public void MainWindow_VerifyEssentialControls()
    {
        var window = _fixture.MainWindow;
        
        var showAllToggle = window.FindFirstDescendant(cf => cf.ByName("Show All"));
        Assert.NotNull(showAllToggle);

        var refreshBtn = window.FindFirstDescendant(cf => cf.ByAutomationId("RefreshBtn"));
        if (refreshBtn == null) 
            refreshBtn = window.FindFirstDescendant(cf => cf.ByHelpText("Refresh Data"));
        
        Assert.NotNull(refreshBtn);
    }

    [Fact]
    public void SettingsWindow_ShouldOpenAndClose()
    {
        var window = _fixture.MainWindow;
        
        // Find Settings button by ToolTip (HelpText)
        var settingsBtn = window.FindFirstDescendant(cf => cf.ByHelpText("Provider Settings")).AsButton();
        settingsBtn.Invoke(); 
        
        // Wait for window with title "Settings"
        var settingsWindow = _fixture.App.GetAllTopLevelWindows(_fixture.Automation).FirstOrDefault(w => w.Title == "Settings");
        
        Assert.NotNull(settingsWindow);
        var cancelBtn = settingsWindow.FindFirstDescendant(cf => cf.ByName("Cancel")).AsButton();
        Assert.NotNull(cancelBtn);
        cancelBtn.Invoke(); 
    }

    [Fact]
    public void MainWindow_ToggleShowAll_ShouldUpdateStatus()
    {
        var window = _fixture.MainWindow;
        var showAllToggle = window.FindFirstDescendant(cf => cf.ByName("Show All")).AsCheckBox();
        
        bool initialState = showAllToggle.IsChecked ?? false;
        showAllToggle.IsChecked = !initialState;
        Assert.NotEqual(initialState, showAllToggle.IsChecked);
        
        // Revert to maintain state for other tests if needed
        showAllToggle.IsChecked = initialState;
    }
}
