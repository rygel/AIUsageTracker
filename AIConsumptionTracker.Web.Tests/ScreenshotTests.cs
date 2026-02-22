using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace AIConsumptionTracker.Web.Tests;

[TestClass]
public class ScreenshotTests : PageTest
{
    private const string BaseUrl = "http://localhost:5100";
    private readonly string _outputDir;

    public ScreenshotTests()
    {
        // bin/Debug/net8.0/../../../docs
        var binPath = AppContext.BaseDirectory;
        // bin/Debug/net8.0 is 3 levels deep from Project. Project is 1 level deep from Solution.
        // So we need to go up 4 levels to get to Solution Root.
        var projectRoot = Path.GetFullPath(Path.Combine(binPath, "../../../../")); 
        _outputDir = Path.Combine(projectRoot, "docs");
        
        Console.WriteLine($"[TEST] Output directory: {_outputDir}");
        
        if (!Directory.Exists(_outputDir))
        {
            Directory.CreateDirectory(_outputDir);
        }
    }

    [TestMethod]
    public async Task CaptureWebScreenshots()
    {
        // 1. Set viewport to a reasonable desktop size
        await Page.SetViewportSizeAsync(1280, 800);

        // 2. Dashboard
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForSelectorAsync(".stat-card, .alert", new() { State = WaitForSelectorState.Visible });
        
        // Take a screenshot of the whole page
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_outputDir, "screenshot_web_dashboard.png"), FullPage = true });

        // 3. Providers List
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Page.WaitForSelectorAsync("table, .alert", new() { State = WaitForSelectorState.Visible });
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_outputDir, "screenshot_web_providers.png"), FullPage = true });

        // 4. Charts
        await Page.GotoAsync($"{BaseUrl}/charts");
        await Page.WaitForSelectorAsync("canvas, .alert", new() { State = WaitForSelectorState.Visible });
        
        // Give chart animation a moment to settle
        await Task.Delay(1000);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_outputDir, "screenshot_web_charts.png"), FullPage = true });
    }
}
