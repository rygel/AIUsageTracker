// <copyright file="ChangelogMarkdownRendererTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Documents;
using System.Windows.Media;
using AIUsageTracker.UI.Slim.Services;

namespace AIUsageTracker.Tests.UI;

public sealed class ChangelogMarkdownRendererTests
{
    // ── GetHeaderLevel ────────────────────────────────────────────────────────
    [Theory]
    [InlineData("# Title", 1)]
    [InlineData("## Section", 2)]
    [InlineData("### Sub", 3)]
    [InlineData("#### Deep", 4)]
    public void GetHeaderLevel_ReturnsCorrectLevel(string line, int expected)
    {
        Assert.Equal(expected, ChangelogMarkdownRenderer.GetHeaderLevel(line));
    }

    [Theory]
    [InlineData("No hashes")]
    [InlineData("#NoSpace")]
    [InlineData("")]
    [InlineData("##NoSpace")]
    public void GetHeaderLevel_ReturnsZero_WhenNotAHeader(string line)
    {
        Assert.Equal(0, ChangelogMarkdownRenderer.GetHeaderLevel(line));
    }

    // ── TryParseNumberedItem ──────────────────────────────────────────────────
    [Fact]
    public void TryParseNumberedItem_ReturnsTrueAndExtractsNumber()
    {
        var result = ChangelogMarkdownRenderer.TryParseNumberedItem("3. Do the thing", out var number, out var content);

        Assert.True(result);
        Assert.Equal(3, number);
        Assert.Equal("Do the thing", content);
    }

    [Fact]
    public void TryParseNumberedItem_ReturnsFalse_ForBulletItem()
    {
        var result = ChangelogMarkdownRenderer.TryParseNumberedItem("- Not numbered", out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryParseNumberedItem_ReturnsFalse_WhenPrefixNotNumeric()
    {
        var result = ChangelogMarkdownRenderer.TryParseNumberedItem("abc. Text", out _, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryParseNumberedItem_ReturnsFalse_WhenContentIsBlank()
    {
        var result = ChangelogMarkdownRenderer.TryParseNumberedItem("1.   ", out _, out _);
        Assert.False(result);
    }

    // ── TryCreateHyperlink ────────────────────────────────────────────────────
    [Fact]
    public void TryCreateHyperlink_ReturnsTrueForValidMarkdownLink()
    {
        var result = ChangelogMarkdownRenderer.TryCreateHyperlink(
            "[Claude](https://claude.ai)", out var hyperlink);

        Assert.True(result);
        Assert.NotNull(hyperlink);
        Assert.Equal(new Uri("https://claude.ai"), hyperlink.NavigateUri);
    }

    [Fact]
    public void TryCreateHyperlink_ReturnsFalse_ForPlainText()
    {
        var result = ChangelogMarkdownRenderer.TryCreateHyperlink("plain text", out _);
        Assert.False(result);
    }

    [Fact]
    public void TryCreateHyperlink_ReturnsFalse_ForRelativeUrl()
    {
        var result = ChangelogMarkdownRenderer.TryCreateHyperlink("[link](/relative)", out _);
        Assert.False(result);
    }

    // ── BuildDocument (WPF — requires STA thread) ─────────────────────────────
    [Fact]
    public void BuildDocument_ReturnsEmptyMessage_ForNullOrWhitespace()
    {
        string? runText = null;
        int? blockCount = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument(string.Empty);
                blockCount = doc.Blocks.Count;
                var paragraph = doc.Blocks.OfType<Paragraph>().Single();
                runText = paragraph.Inlines.OfType<Run>().Single().Text;
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.Equal(1, blockCount);
        Assert.Contains("No changelog", runText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildDocument_ParsesHeadingBulletAndParagraph()
    {
        const string markdown = """
            # Release 1.0

            A new version.

            - Fix one
            - Fix two
            """;

        int? totalBlocks = null;
        double? headingFontSize = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument(markdown);
                totalBlocks = doc.Blocks.Count;
                headingFontSize = doc.Blocks.OfType<Paragraph>().First().FontSize;
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.Equal(22, headingFontSize); // h1 = 22
        Assert.Equal(4, totalBlocks);     // heading + paragraph + 2 bullets
    }

    [Fact]
    public void BuildDocument_ParsesFencedCodeBlock()
    {
        const string markdown = """
            Some intro.

            ```
            var x = 1;
            ```
            """;

        int? blockCount = null;
        string? lastFontFamilySource = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument(markdown);
                blockCount = doc.Blocks.Count;
                lastFontFamilySource = doc.Blocks.OfType<Paragraph>().Last().FontFamily.Source;
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.Equal(2, blockCount); // intro paragraph + code block
        Assert.Equal("Consolas", lastFontFamilySource);
    }

    // ── Inline formatting (STA thread required) ───────────────────────────────
    [Fact]
    public void BuildDocument_RendersBoldInline()
    {
        string? boldText = null;
        bool? hasBold = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument("Release includes **major fix** today.");
                var paragraph = doc.Blocks.OfType<Paragraph>().Single();
                var bold = paragraph.Inlines.OfType<Bold>().FirstOrDefault();
                hasBold = bold != null;
                boldText = bold?.Inlines.OfType<Run>().FirstOrDefault()?.Text;
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.True(hasBold, "Expected a Bold inline");
        Assert.Equal("major fix", boldText);
    }

    [Fact]
    public void BuildDocument_RendersItalicInline()
    {
        string? italicText = null;
        bool? hasItalic = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument("Note: *emphasis here* for clarity.");
                var paragraph = doc.Blocks.OfType<Paragraph>().Single();
                var italic = paragraph.Inlines.OfType<Italic>().FirstOrDefault();
                hasItalic = italic != null;
                italicText = italic?.Inlines.OfType<Run>().FirstOrDefault()?.Text;
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.True(hasItalic, "Expected an Italic inline");
        Assert.Equal("emphasis here", italicText);
    }

    [Fact]
    public void BuildDocument_RendersInlineCode_WithConsolasFont()
    {
        string? inlineCodeText = null;
        string? inlineCodeFont = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument("Run `dotnet test` to execute.");
                var paragraph = doc.Blocks.OfType<Paragraph>().Single();
                var codeRun = paragraph.Inlines.OfType<Run>()
                    .FirstOrDefault(r => string.Equals(r.FontFamily?.Source, "Consolas", StringComparison.Ordinal));
                inlineCodeText = codeRun?.Text;
                inlineCodeFont = codeRun?.FontFamily?.Source;
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.Equal("dotnet test", inlineCodeText);
        Assert.Equal("Consolas", inlineCodeFont);
    }

    [Fact]
    public void BuildDocument_RendersMixedInlines_InCorrectOrder()
    {
        // "Fix **bold** and `code` together." should produce:
        // Run("Fix "), Bold("bold"), Run(" and "), Run("code" w/ Consolas), Run(" together.")
        int? inlineCount = null;
        bool? hasBold = null;
        bool? hasConsolas = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument("Fix **bold** and `code` together.");
                var paragraph = doc.Blocks.OfType<Paragraph>().Single();
                inlineCount = paragraph.Inlines.Count;
                hasBold = paragraph.Inlines.OfType<Bold>().Any();
                hasConsolas = paragraph.Inlines.OfType<Run>()
                    .Any(r => string.Equals(r.FontFamily?.Source, "Consolas", StringComparison.Ordinal));
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.True(hasBold, "Expected a Bold inline");
        Assert.True(hasConsolas, "Expected an inline code Run with Consolas font");
        Assert.True(inlineCount >= 3, "Expected at least 3 inlines for mixed content");
    }

    [Fact]
    public void BuildDocument_RendersHyperlinkInDocument()
    {
        Uri? linkUri = null;
        string? linkText = null;
        Exception? ex = null;

        var thread = new Thread(() =>
        {
            try
            {
                var renderer = new ChangelogMarkdownRenderer((_, fallback) => fallback);
                var doc = renderer.BuildDocument("See [release notes](https://example.com/notes) for details.");
                var paragraph = doc.Blocks.OfType<Paragraph>().Single();
                var hyperlink = paragraph.Inlines.OfType<Hyperlink>().FirstOrDefault();
                linkUri = hyperlink?.NavigateUri;
                linkText = hyperlink?.Inlines.OfType<Run>().FirstOrDefault()?.Text;
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            throw new Exception("STA thread threw", ex);
        }

        Assert.Equal(new Uri("https://example.com/notes"), linkUri);
        Assert.Equal("release notes", linkText);
    }
}
