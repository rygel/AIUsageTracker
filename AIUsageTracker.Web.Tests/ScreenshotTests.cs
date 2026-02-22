using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace AIUsageTracker.Web.Tests;

[TestClass]
public class ScreenshotTests : PageTest
{
    private const string BaseUrl = "http://127.0.0.1:5100";
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
    public async Task Dashboard_StylesheetAssetsLoadAndStylesApply()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForSelectorAsync(".sidebar", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var siteCssStatus = await Page.EvaluateAsync<int>("""
            async () => {
                const res = await fetch('/css/site.css', { cache: 'no-store' });
                return res.status;
            }
            """);
        var themesCssStatus = await Page.EvaluateAsync<int>("""
            async () => {
                const res = await fetch('/css/themes.css', { cache: 'no-store' });
                return res.status;
            }
            """);

        Assert.AreEqual(200, siteCssStatus, "Expected /css/site.css to be served.");
        Assert.AreEqual(200, themesCssStatus, "Expected /css/themes.css to be served.");

        var sidebarPosition = await Page.EvaluateAsync<string>("""
            () => getComputedStyle(document.querySelector('.sidebar')).position
            """);
        var sidebarWidth = await Page.EvaluateAsync<string>("""
            () => getComputedStyle(document.querySelector('.sidebar')).width
            """);
        var appContainerDisplay = await Page.EvaluateAsync<string>("""
            () => getComputedStyle(document.querySelector('.app-container')).display
            """);
        var footerText = await Page.TextContentAsync(".sidebar-footer-text");

        Assert.AreEqual("fixed", sidebarPosition, "Sidebar CSS is not applied (expected fixed sidebar). ");
        Assert.AreEqual("flex", appContainerDisplay, "Layout CSS is not applied (expected flex app container).");
        Assert.AreEqual("200px", sidebarWidth, "Sidebar width does not match expected styled layout.");
        Assert.IsFalse(string.IsNullOrWhiteSpace(footerText), "Footer text should be present.");
        StringAssert.Contains(footerText, "v", "Footer should include Web UI version string.");
    }

    [TestMethod]
    public async Task CaptureWebScreenshots()
    {
        // Capture console logs for debugging CI
        Page.Console += (_, e) => Console.WriteLine($"[BROWSER] {e.Type}: {e.Text}");
        Page.PageError += (_, e) => Console.WriteLine($"[BROWSER ERROR] {e}");

        // 1. Set viewport to a reasonable desktop size
        await Page.SetViewportSizeAsync(1280, 800);

        // 2. Dashboard
        Console.WriteLine("[TEST] Navigating to Dashboard...");
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForSelectorAsync(".stat-card, .alert", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_outputDir, "screenshot_web_dashboard.png"), FullPage = true });

        // 3. Providers List
        Console.WriteLine("[TEST] Navigating to Providers...");
        await Page.GotoAsync($"{BaseUrl}/providers");
        await Page.WaitForSelectorAsync("table, .alert", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_outputDir, "screenshot_web_providers.png"), FullPage = true });

        // 4. Charts
        Console.WriteLine("[TEST] Navigating to Charts...");
        await Page.GotoAsync($"{BaseUrl}/charts");
        
        // Wait for either the canvas (if data exists) or the info alert (if no data)
        // We also wait for the container itself to be sure the page structure is there
        await Page.WaitForSelectorAsync(".chart-container, .alert", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        
        // Give chart animation a moment to settle
        await Task.Delay(2000);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(_outputDir, "screenshot_web_charts.png"), FullPage = true });
        Console.WriteLine("[TEST] Completed all screenshots.");
    }
}

