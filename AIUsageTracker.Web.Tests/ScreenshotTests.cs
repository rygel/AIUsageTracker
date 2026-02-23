using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIUsageTracker.Web.Tests;

[TestClass]
public class ScreenshotTests : PageTest
{
    private const string BaseUrl = "http://127.0.0.1:5100";
    private sealed class ThemeCatalog
    {
        [JsonPropertyName("themes")]
        public List<ThemeCatalogEntry> Themes { get; set; } = [];
    }

    private sealed class ThemeCatalogEntry
    {
        [JsonPropertyName("webKey")]
        public string WebKey { get; set; } = string.Empty;

        [JsonPropertyName("representative")]
        public bool Representative { get; set; }

        [JsonPropertyName("tokens")]
        public ThemeTokenEntry? Tokens { get; set; }
    }

    private sealed class ThemeTokenEntry
    {
        [JsonPropertyName("bgPrimary")]
        public string BgPrimary { get; set; } = string.Empty;

        [JsonPropertyName("accentPrimary")]
        public string AccentPrimary { get; set; } = string.Empty;
    }

    private readonly string _projectRoot;
    private readonly string[] _expectedThemes;
    private readonly Dictionary<string, (string BgPrimary, string AccentPrimary)> _representativeThemeTokens;
    private readonly string _outputDir;
    private readonly string _themeOutputDir;

    public ScreenshotTests()
    {
        // bin/Debug/net8.0/../../../docs
        var binPath = AppContext.BaseDirectory;
        // bin/Debug/net8.0 is 3 levels deep from Project. Project is 1 level deep from Solution.
        // So we need to go up 4 levels to get to Solution Root.
        _projectRoot = Path.GetFullPath(Path.Combine(binPath, "../../../../"));
        _outputDir = Path.Combine(_projectRoot, "docs");
        _themeOutputDir = Path.Combine(Path.GetTempPath(), "AIUsageTracker", "web-theme-smoke");

        var catalog = LoadThemeCatalog(_projectRoot);
        _expectedThemes = catalog.Themes.Select(t => t.WebKey).ToArray();
        _representativeThemeTokens = catalog.Themes
            .Where(t => t.Representative)
            .Where(t => t.Tokens is not null)
            .ToDictionary(
                t => t.WebKey,
                t => (
                    t.Tokens!.BgPrimary.Trim().ToLowerInvariant(),
                    t.Tokens!.AccentPrimary.Trim().ToLowerInvariant()),
                StringComparer.Ordinal);
        
        Console.WriteLine($"[TEST] Output directory: {_outputDir}");
        
        if (!Directory.Exists(_outputDir))
        {
            Directory.CreateDirectory(_outputDir);
        }

        if (!Directory.Exists(_themeOutputDir))
        {
            Directory.CreateDirectory(_themeOutputDir);
        }
    }

    private static ThemeCatalog LoadThemeCatalog(string projectRoot)
    {
        var manifestPath = Path.Combine(projectRoot, "design", "theme-catalog.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Theme catalog manifest not found.", manifestPath);
        }

        var json = File.ReadAllText(manifestPath);
        var catalog = JsonSerializer.Deserialize<ThemeCatalog>(json);
        if (catalog is null || catalog.Themes.Count == 0)
        {
            throw new InvalidOperationException("Theme catalog manifest is empty or invalid.");
        }

        return catalog;
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

    [TestMethod]
    public async Task ThemeSelector_AppliesAllThemes()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForSelectorAsync("#theme-select", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var availableThemes = await Page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('#theme-select option')).map(o => o.value)
            """);

        CollectionAssert.AreEquivalent(_expectedThemes, availableThemes, "Theme selector options mismatch expected catalog.");

        foreach (var theme in _expectedThemes)
        {
            await Page.EvaluateAsync("""
                (theme) => {
                    const select = document.getElementById('theme-select');
                    if (!select) {
                        throw new Error('theme-select not found');
                    }

                    select.value = theme;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """, theme);

            var appliedTheme = await Page.EvaluateAsync<string>("""
                () => document.documentElement.getAttribute('data-theme') || ''
                """);
            var bgPrimary = await Page.EvaluateAsync<string>("""
                () => getComputedStyle(document.documentElement).getPropertyValue('--bg-primary').trim()
                """);

            Assert.AreEqual(theme, appliedTheme, $"Theme '{theme}' was not applied to data-theme.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(bgPrimary), $"Theme '{theme}' did not resolve --bg-primary.");
        }
    }

    [TestMethod]
    public async Task RepresentativeThemes_RenderDistinctVisualSnapshots()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForSelectorAsync("#theme-select", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var representativeThemes = _representativeThemeTokens.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var screenshotPaths = new List<string>();

        foreach (var theme in representativeThemes)
        {
            await Page.EvaluateAsync("""
                (theme) => {
                    const select = document.getElementById('theme-select');
                    if (!select) {
                        throw new Error('theme-select not found');
                    }

                    select.value = theme;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """, theme);

            await Task.Delay(300);

            var appliedTheme = await Page.EvaluateAsync<string>("""
                () => document.documentElement.getAttribute('data-theme') || ''
                """);
            Assert.AreEqual(theme, appliedTheme, $"Theme '{theme}' was not applied before screenshot capture.");

            var filePath = Path.Combine(_themeOutputDir, $"screenshot_web_theme_{theme}.png");
            await Page.ScreenshotAsync(new() { Path = filePath, FullPage = true });
            screenshotPaths.Add(filePath);

            var fileInfo = new FileInfo(filePath);
            Assert.IsTrue(fileInfo.Exists, $"Screenshot not created for theme '{theme}'.");
            Assert.IsTrue(fileInfo.Length > 25_000, $"Screenshot too small for theme '{theme}', likely render failure.");
        }

        var distinctHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in screenshotPaths)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = await File.ReadAllBytesAsync(path);
            var hash = Convert.ToHexString(sha.ComputeHash(bytes));
            distinctHashes.Add(hash);
        }

        Assert.AreEqual(screenshotPaths.Count, distinctHashes.Count, "Representative theme screenshots should be visually distinct.");
    }

    [TestMethod]
    public async Task RepresentativeThemes_ExposeExpectedCssTokens()
    {
        await Page.SetViewportSizeAsync(1280, 800);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForSelectorAsync("#theme-select", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        foreach (var (theme, expectedTokens) in _representativeThemeTokens)
        {
            await Page.EvaluateAsync("""
                (theme) => {
                    const select = document.getElementById('theme-select');
                    if (!select) {
                        throw new Error('theme-select not found');
                    }

                    select.value = theme;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """, theme);

            var appliedTheme = await Page.EvaluateAsync<string>("""
                () => document.documentElement.getAttribute('data-theme') || ''
                """);
            Assert.AreEqual(theme, appliedTheme, $"Theme '{theme}' was not applied before CSS token assertions.");

            var tokens = await Page.EvaluateAsync<string[]?>("""
                () => {
                    const rootStyle = getComputedStyle(document.documentElement);
                    const bg = rootStyle.getPropertyValue('--bg-primary').trim().toLowerCase();
                    const accent = rootStyle.getPropertyValue('--accent-primary').trim().toLowerCase();
                    return [bg, accent];
                }
                """);

            Assert.IsNotNull(tokens, $"Theme '{theme}' token payload should not be null.");
            Assert.AreEqual(2, tokens.Length, $"Theme '{theme}' should return two token values.");
            Assert.AreEqual(expectedTokens.BgPrimary, tokens[0], $"Theme '{theme}' unexpected --bg-primary.");
            Assert.AreEqual(expectedTokens.AccentPrimary, tokens[1], $"Theme '{theme}' unexpected --accent-primary.");
        }
    }
}

