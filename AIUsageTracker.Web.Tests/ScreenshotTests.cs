using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AIUsageTracker.Web.Tests;

[TestClass]
public class ScreenshotTests : WebTestBase
{
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

    private sealed class PlaywrightSession : IAsyncDisposable
    {
        private readonly IPlaywright _playwright;
        private readonly IBrowser _browser;
        private readonly IBrowserContext _context;

        public IPage Page { get; }

        public PlaywrightSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page)
        {
            this._playwright = playwright;
            this._browser = browser;
            this._context = context;
            this.Page = page;
        }

        public async ValueTask DisposeAsync()
        {
            await this.Page.CloseAsync().ConfigureAwait(false);
            await this._context.CloseAsync().ConfigureAwait(false);
            await this._browser.CloseAsync().ConfigureAwait(false);
            this._playwright.Dispose();
        }
    }

    private readonly string _projectRoot;
    private readonly string[] _expectedThemes;
    private readonly Dictionary<string, (string BgPrimary, string AccentPrimary)> _representativeThemeTokens;
    private readonly string _outputDir;
    private readonly string _themeOutputDir;

    private const int ThemeSwitchDelayMs = 300;
    private const int MinThemeScreenshotBytes = 25_000;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void EnsureFactoryInitialized(TestContext context)
    {
        InitializeFactory(context);
    }

    public ScreenshotTests()
    {
        var binPath = AppContext.BaseDirectory;
        this._projectRoot = Path.GetFullPath(Path.Combine(binPath, "../../../../"));
        this._outputDir = Path.Combine(this._projectRoot, "docs");
        this._themeOutputDir = Path.Combine(Path.GetTempPath(), "AIUsageTracker", "web-theme-smoke");

        var catalog = LoadThemeCatalog(this._projectRoot);
        this._expectedThemes = catalog.Themes.Select(t => t.WebKey).ToArray();
        this._representativeThemeTokens = catalog.Themes
            .Where(t => t.Representative)
            .Where(t => t.Tokens is not null)
            .ToDictionary(
                t => t.WebKey,
                t => (
                    t.Tokens!.BgPrimary.Trim().ToLowerInvariant(),
                    t.Tokens!.AccentPrimary.Trim().ToLowerInvariant()),
                StringComparer.Ordinal);

        Console.WriteLine($"[TEST] Output directory: {this._outputDir}");

        if (!Directory.Exists(this._outputDir))
        {
            Directory.CreateDirectory(this._outputDir);
        }

        if (!Directory.Exists(this._themeOutputDir))
        {
            Directory.CreateDirectory(this._themeOutputDir);
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

    private static bool IsPermissionError(Win32Exception ex)
    {
        return ex.NativeErrorCode is 5 or 13;
    }

    private static double ContrastRatio(string hex1, string hex2)
    {
        static (double R, double G, double B) ParseHex(string hex)
        {
            var value = hex.Trim();
            if (value.StartsWith('#'))
            {
                value = value[1..];
            }

            if (value.Length != 6)
            {
                throw new InvalidOperationException($"Expected 6-digit hex color, got '{hex}'.");
            }

            var r = Convert.ToInt32(value[..2], 16) / 255.0;
            var g = Convert.ToInt32(value.Substring(2, 2), 16) / 255.0;
            var b = Convert.ToInt32(value.Substring(4, 2), 16) / 255.0;
            return (r, g, b);
        }

        static double ToLinear(double channel)
        {
            return channel <= 0.03928
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        static double Luminance((double R, double G, double B) rgb)
        {
            var r = ToLinear(rgb.R);
            var g = ToLinear(rgb.G);
            var b = ToLinear(rgb.B);
            return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        }

        var l1 = Luminance(ParseHex(hex1));
        var l2 = Luminance(ParseHex(hex2));
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static async Task<PlaywrightSession?> TryCreateBrowserSessionAsync(string testName)
    {
        try
        {
            var playwright = await Playwright.CreateAsync().ConfigureAwait(false);
            var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true }).ConfigureAwait(false);
            var context = await browser.NewContextAsync().ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);

            return new PlaywrightSession(playwright, browser, context, page);
        }
        catch (PlaywrightException ex)
        {
            Console.WriteLine($"[SKIP] Playwright unavailable for {testName}: {ex.Message}");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"[SKIP] Playwright unavailable for {testName}: {ex.Message}");
            return null;
        }
        catch (Win32Exception ex) when (IsPermissionError(ex))
        {
            Console.WriteLine($"[SKIP] Playwright unavailable for {testName}: {ex.Message}");
            return null;
        }
    }

    private static void SkipBrowserTest(string testName)
    {
        Assert.Inconclusive($"Playwright is not available in this environment. Skipping '{testName}'.");
    }

    [TestMethod]
    public async Task Dashboard_StylesheetAssetsLoadAndStylesApply()
    {
        await using var browserSession = await TryCreateBrowserSessionAsync(nameof(Dashboard_StylesheetAssetsLoadAndStylesApply));
        if (browserSession is null)
        {
            SkipBrowserTest(nameof(Dashboard_StylesheetAssetsLoadAndStylesApply));
            return;
        }

        var page = browserSession.Page;
        await page.SetViewportSizeAsync(1280, 800);
        await page.GotoAsync(ServerUrl);
        await page.WaitForSelectorAsync(".sidebar", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var siteCssStatus = await page.EvaluateAsync<int>("""
            async () => {
                const res = await fetch('/css/site.css', { cache: 'no-store' });
                return res.status;
            }
            """);
        var themesCssStatus = await page.EvaluateAsync<int>("""
            async () => {
                const res = await fetch('/css/themes.css', { cache: 'no-store' });
                return res.status;
            }
            """);

        Assert.AreEqual(200, siteCssStatus, "Expected /css/site.css to be served.");
        Assert.AreEqual(200, themesCssStatus, "Expected /css/themes.css to be served.");

        var sidebarPosition = await page.EvaluateAsync<string>("""
            () => getComputedStyle(document.querySelector('.sidebar')).position
            """);
        var sidebarWidth = await page.EvaluateAsync<string>("""
            () => getComputedStyle(document.querySelector('.sidebar')).width
            """);
        var appContainerDisplay = await page.EvaluateAsync<string>("""
            () => getComputedStyle(document.querySelector('.app-container')).display
            """);
        var footerText = await page.TextContentAsync(".sidebar-footer-text");

        Assert.AreEqual("fixed", sidebarPosition, "Sidebar CSS is not applied (expected fixed sidebar). ");
        Assert.AreEqual("flex", appContainerDisplay, "Layout CSS is not applied (expected flex app container).");
        Assert.AreEqual("200px", sidebarWidth, "Sidebar width does not match expected styled layout.");
        Assert.IsNotNull(footerText, "Footer text should be present.");
        Assert.IsTrue(
            footerText.Contains("v", StringComparison.Ordinal),
            "Footer should include Web UI version string.");
    }

    [TestMethod]
    public async Task Dashboard_ReliabilityPanelStylesAndMarkupArePresent()
    {
        await using var browserSession = await TryCreateBrowserSessionAsync(nameof(Dashboard_ReliabilityPanelStylesAndMarkupArePresent));
        if (browserSession is null)
        {
            SkipBrowserTest(nameof(Dashboard_ReliabilityPanelStylesAndMarkupArePresent));
            return;
        }

        var page = browserSession.Page;
        await page.SetViewportSizeAsync(1280, 800);
        await page.GotoAsync(ServerUrl);
        await page.WaitForSelectorAsync(".sidebar", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var cssText = await page.EvaluateAsync<string>("""
            async () => {
                const res = await fetch('/css/site.css', { cache: 'no-store' });
                return await res.text();
            }
            """);

        Assert.IsTrue(
            cssText.Contains(".reliability-grid", StringComparison.Ordinal),
            "Reliability grid CSS hook missing.");
        Assert.IsTrue(
            cssText.Contains(".reliability-card", StringComparison.Ordinal),
            "Reliability card CSS hook missing.");
        Assert.IsTrue(
            cssText.Contains(".reliability-badge", StringComparison.Ordinal),
            "Reliability badge CSS hook missing.");

        var providerCardCount = await page.EvaluateAsync<int>("""
            () => document.querySelectorAll('.provider-card').length
            """);

        if (providerCardCount > 0)
        {
            var reliabilityCardCount = await page.EvaluateAsync<int>("""
                () => document.querySelectorAll('.reliability-card').length
                """);
            var reliabilityHeading = await page.EvaluateAsync<string?>("""
                () => {
                    const heading = Array.from(document.querySelectorAll('h2'))
                        .find(h => h.textContent?.trim() === 'Provider Reliability');
                    return heading?.textContent?.trim() ?? null;
                }
                """);

            Assert.IsTrue(reliabilityCardCount > 0, "Reliability cards should render when provider cards are present.");
            Assert.AreEqual("Provider Reliability", reliabilityHeading, "Reliability section heading missing.");
        }
    }

    [TestMethod]
    public async Task CaptureWebScreenshots()
    {
        await using var browserSession = await TryCreateBrowserSessionAsync(nameof(CaptureWebScreenshots));
        if (browserSession is null)
        {
            SkipBrowserTest(nameof(CaptureWebScreenshots));
            return;
        }

        var page = browserSession.Page;
        page.Console += (_, e) => Console.WriteLine($"[BROWSER] {e.Type}: {e.Text}");
        page.PageError += (_, e) => Console.WriteLine($"[BROWSER ERROR] {e}");

        // 1. Set viewport to a reasonable desktop size
        await page.SetViewportSizeAsync(1280, 800);

        // 2. Dashboard
        Console.WriteLine("[TEST] Navigating to Dashboard...");
        await page.GotoAsync(ServerUrl);
        await page.WaitForSelectorAsync(".stat-card, .alert", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.ScreenshotAsync(new() { Path = Path.Combine(this._outputDir, "screenshot_web_dashboard.png"), FullPage = true });

        // 3. Providers List
        Console.WriteLine("[TEST] Navigating to Providers...");
        await page.GotoAsync($"{ServerUrl}/providers");
        await page.WaitForSelectorAsync("table, .alert", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.ScreenshotAsync(new() { Path = Path.Combine(this._outputDir, "screenshot_web_providers.png"), FullPage = true });

        // 4. Charts
        Console.WriteLine("[TEST] Navigating to Charts...");
        await page.GotoAsync($"{ServerUrl}/charts");

        await page.WaitForSelectorAsync(".chart-container, .alert", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await Task.Delay(2000);
        await page.ScreenshotAsync(new() { Path = Path.Combine(this._outputDir, "screenshot_web_charts.png"), FullPage = true });
        Console.WriteLine("[TEST] Completed all screenshots.");
    }

    [TestMethod]
    public async Task ThemeSelector_AppliesAllThemes()
    {
        await using var browserSession = await TryCreateBrowserSessionAsync(nameof(ThemeSelector_AppliesAllThemes));
        if (browserSession is null)
        {
            SkipBrowserTest(nameof(ThemeSelector_AppliesAllThemes));
            return;
        }

        var page = browserSession.Page;
        await page.SetViewportSizeAsync(1280, 800);
        await page.GotoAsync(ServerUrl);
        await page.WaitForSelectorAsync("#theme-select", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var availableThemes = await page.EvaluateAsync<string[]>("""
            () => Array.from(document.querySelectorAll('#theme-select option')).map(o => o.value)
            """);

        CollectionAssert.AreEquivalent(this._expectedThemes, availableThemes, "Theme selector options mismatch expected catalog.");

        foreach (var theme in this._expectedThemes)
        {
            await page.EvaluateAsync("""
                (theme) => {
                    const select = document.getElementById('theme-select');
                    if (!select) {
                        throw new Error('theme-select not found');
                    }

                    select.value = theme;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """,
                theme);

            var appliedTheme = await page.EvaluateAsync<string>("""
                () => document.documentElement.getAttribute('data-theme') || ''
                """);
            var bgPrimary = await page.EvaluateAsync<string>("""
                () => getComputedStyle(document.documentElement).getPropertyValue('--bg-primary').trim()
                """);

            Assert.AreEqual(theme, appliedTheme, $"Theme '{theme}' was not applied to data-theme.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(bgPrimary), $"Theme '{theme}' did not resolve --bg-primary.");
        }
    }

    [TestMethod]
    public async Task RepresentativeThemes_RenderDistinctVisualSnapshots()
    {
        await using var browserSession = await TryCreateBrowserSessionAsync(nameof(RepresentativeThemes_RenderDistinctVisualSnapshots));
        if (browserSession is null)
        {
            SkipBrowserTest(nameof(RepresentativeThemes_RenderDistinctVisualSnapshots));
            return;
        }

        var page = browserSession.Page;
        await page.SetViewportSizeAsync(1280, 800);
        await page.GotoAsync(ServerUrl);
        await page.WaitForSelectorAsync("#theme-select", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var representativeThemes = this._representativeThemeTokens.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var screenshotPaths = new List<string>();

        foreach (var theme in representativeThemes)
        {
            await page.EvaluateAsync("""
                (theme) => {
                    const select = document.getElementById('theme-select');
                    if (!select) {
                        throw new Error('theme-select not found');
                    }

                    select.value = theme;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """,
                theme);

            await Task.Delay(ThemeSwitchDelayMs);

            var appliedTheme = await page.EvaluateAsync<string>("""
                () => document.documentElement.getAttribute('data-theme') || ''
                """);
            Assert.AreEqual(theme, appliedTheme, $"Theme '{theme}' was not applied before screenshot capture.");

            var filePath = Path.Combine(this._themeOutputDir, $"screenshot_web_theme_{theme}.png");
            await page.ScreenshotAsync(new() { Path = filePath, FullPage = true });
            screenshotPaths.Add(filePath);

            var fileInfo = new FileInfo(filePath);
            Assert.IsTrue(fileInfo.Exists, $"Screenshot not created for theme '{theme}'.");
            Assert.IsTrue(fileInfo.Length > MinThemeScreenshotBytes, $"Screenshot too small for theme '{theme}', likely render failure.");
        }

        var distinctHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in screenshotPaths)
        {
            using var sha = SHA256.Create();
            var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            var hash = Convert.ToHexString(sha.ComputeHash(bytes));
            distinctHashes.Add(hash);
        }

        Assert.AreEqual(screenshotPaths.Count, distinctHashes.Count, "Representative theme screenshots should be visually distinct.");
    }

    [TestMethod]
    public async Task RepresentativeThemes_ExposeExpectedCssTokens()
    {
        await using var browserSession = await TryCreateBrowserSessionAsync(nameof(RepresentativeThemes_ExposeExpectedCssTokens));
        if (browserSession is null)
        {
            SkipBrowserTest(nameof(RepresentativeThemes_ExposeExpectedCssTokens));
            return;
        }

        var page = browserSession.Page;
        await page.SetViewportSizeAsync(1280, 800);
        await page.GotoAsync(ServerUrl);
        await page.WaitForSelectorAsync("#theme-select", new() { State = WaitForSelectorState.Visible, Timeout = 15000 });

        foreach (var (theme, expectedTokens) in this._representativeThemeTokens)
        {
            await page.EvaluateAsync("""
                (theme) => {
                    const select = document.getElementById('theme-select');
                    if (!select) {
                        throw new Error('theme-select not found');
                    }

                    select.value = theme;
                    select.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """,
                theme);

            var appliedTheme = await page.EvaluateAsync<string>("""
                () => document.documentElement.getAttribute('data-theme') || ''
                """);
            Assert.AreEqual(theme, appliedTheme, $"Theme '{theme}' was not applied before CSS token assertions.");

            var tokens = await page.EvaluateAsync<string[]>("""
                () => {
                    const rootStyle = getComputedStyle(document.documentElement);
                    const bg = rootStyle.getPropertyValue('--bg-primary').trim().toLowerCase();
                    const accent = rootStyle.getPropertyValue('--accent-primary').trim().toLowerCase();
                    const text = rootStyle.getPropertyValue('--text-primary').trim().toLowerCase();
                    return [bg, accent, text];
                }
                """);

            Assert.IsNotNull(tokens, $"Theme '{theme}' token payload should not be null.");
            Assert.AreEqual(3, tokens.Length, $"Theme '{theme}' should return three token values.");
            Assert.AreEqual(expectedTokens.BgPrimary, tokens[0], $"Theme '{theme}' unexpected --bg-primary.");
            Assert.AreEqual(expectedTokens.AccentPrimary, tokens[1], $"Theme '{theme}' unexpected --accent-primary.");

            var contrast = ContrastRatio(tokens[2], tokens[0]);
            Assert.IsTrue(contrast >= 4.5, $"Theme '{theme}' has insufficient text/background contrast ({contrast:F2}).");
        }
    }
}
