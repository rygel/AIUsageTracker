// <copyright file="ChangelogMarkdownRenderer.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Converts a Markdown string into a WPF <see cref="FlowDocument"/> for display in the changelog window.
/// Supports: headings (#, ##, ###), bullet lists (- / *), numbered lists, inline bold/italic/code,
/// fenced code blocks (```), and Markdown hyperlinks [text](url).
/// </summary>
internal sealed class ChangelogMarkdownRenderer
{
    private static readonly Regex TokenRegex = new(
        @"(\*\*[^*]+\*\*|`[^`]+`|\*[^*]+\*|\[[^\]]+\]\([^)]+\))",
        RegexOptions.Compiled | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private readonly Func<string, SolidColorBrush, SolidColorBrush> _resolveResourceBrush;

    /// <param name="resolveResourceBrush">
    /// Resolves a named theme resource to a brush, falling back to the supplied default.
    /// Typically <c>this.GetResourceBrush</c> from the host <see cref="System.Windows.Window"/>.
    /// </param>
    public ChangelogMarkdownRenderer(Func<string, SolidColorBrush, SolidColorBrush> resolveResourceBrush)
    {
        this._resolveResourceBrush = resolveResourceBrush;
    }

    /// <summary>Renders <paramref name="markdown"/> into a <see cref="FlowDocument"/>.</summary>
    /// <returns></returns>
    public FlowDocument BuildDocument(string markdown)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            PagePadding = new Thickness(16),
            Background = Brushes.Transparent,
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            document.Blocks.Add(new Paragraph(new Run("No changelog available for this release."))
            {
                FontStyle = FontStyles.Italic,
            });
            return document;
        }

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var inCodeBlock = false;
        var codeBuilder = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var line = rawLine ?? string.Empty;
            var trimmed = line.Trim();

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (inCodeBlock)
                {
                    this.AddCodeBlock(document, codeBuilder.ToString().TrimEnd());
                    codeBuilder.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    inCodeBlock = true;
                }

                continue;
            }

            if (inCodeBlock)
            {
                codeBuilder.AppendLine(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            this.ProcessContentLine(document, trimmed);
        }

        if (inCodeBlock && codeBuilder.Length > 0)
        {
            this.AddCodeBlock(document, codeBuilder.ToString().TrimEnd());
        }

        return document;
    }

    private void ProcessContentLine(FlowDocument document, string trimmed)
    {
        var headerLevel = GetHeaderLevel(trimmed);
        if (headerLevel > 0)
        {
            var headerText = trimmed[(headerLevel + 1)..];
            var header = new Paragraph
            {
                Margin = new Thickness(0, headerLevel == 1 ? 10 : 6, 0, 4),
                FontWeight = FontWeights.SemiBold,
                FontSize = headerLevel switch
                {
                    1 => 22,
                    2 => 18,
                    3 => 16,
                    _ => 14,
                },
            };
            this.AddMarkdownInlines(header, headerText);
            document.Blocks.Add(header);
            return;
        }

        if (trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            var bullet = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            bullet.Inlines.Add(new Run("• "));
            this.AddMarkdownInlines(bullet, trimmed[2..]);
            document.Blocks.Add(bullet);
            return;
        }

        if (TryParseNumberedItem(trimmed, out var numberedPrefix, out var numberedText))
        {
            var numbered = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
            numbered.Inlines.Add(new Run($"{numberedPrefix}. "));
            this.AddMarkdownInlines(numbered, numberedText);
            document.Blocks.Add(numbered);
            return;
        }

        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 6), LineHeight = 20 };
        this.AddMarkdownInlines(paragraph, trimmed);
        document.Blocks.Add(paragraph);
    }

    internal static int GetHeaderLevel(string trimmedLine)
    {
        var level = 0;
        while (level < trimmedLine.Length && trimmedLine[level] == '#')
        {
            level++;
        }

        return level > 0 && level < trimmedLine.Length && trimmedLine[level] == ' ' ? level : 0;
    }

    internal static bool TryParseNumberedItem(string line, out int number, out string content)
    {
        number = 0;
        content = string.Empty;

        var dotIndex = line.IndexOf(". ", StringComparison.Ordinal);
        if (dotIndex <= 0)
        {
            return false;
        }

        var prefix = line[..dotIndex];
        if (!int.TryParse(prefix, System.Globalization.CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        content = line[(dotIndex + 2)..];
        return !string.IsNullOrWhiteSpace(content);
    }

    internal static bool TryCreateHyperlink(string token, out Hyperlink hyperlink)
    {
        hyperlink = null!;

        if (!token.StartsWith("[", StringComparison.Ordinal) || !token.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var separator = token.IndexOf("](", StringComparison.Ordinal);
        if (separator <= 1)
        {
            return false;
        }

        var text = token[1..separator];
        var url = token[(separator + 2)..^1];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        hyperlink = new Hyperlink(new Run(text)) { NavigateUri = uri };
        hyperlink.Click += (_, _) =>
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        };

        return true;
    }

    private void AddCodeBlock(FlowDocument document, string codeText)
    {
        var codeParagraph = new Paragraph(new Run(codeText))
        {
            Margin = new Thickness(0, 6, 0, 10),
            Padding = new Thickness(10, 8, 10, 8),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Background = this._resolveResourceBrush("FooterBackground", Brushes.Black),
            Foreground = this._resolveResourceBrush("PrimaryText", Brushes.White),
        };
        document.Blocks.Add(codeParagraph);
    }

    private void AddMarkdownInlines(Paragraph paragraph, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var matches = TokenRegex.Matches(text);
        var cursor = 0;

        foreach (Match match in matches)
        {
            if (match.Index > cursor)
            {
                paragraph.Inlines.Add(new Run(text[cursor..match.Index]));
            }

            var token = match.Value;
            if (TryCreateHyperlink(token, out var hyperlink))
            {
                paragraph.Inlines.Add(hyperlink);
            }
            else if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Bold(new Run(token[2..^2])));
            }
            else if (token.StartsWith("*", StringComparison.Ordinal) && token.EndsWith("*", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Italic(new Run(token[1..^1])));
            }
            else if (token.StartsWith("`", StringComparison.Ordinal) && token.EndsWith("`", StringComparison.Ordinal))
            {
                paragraph.Inlines.Add(new Run(token[1..^1])
                {
                    FontFamily = new FontFamily("Consolas"),
                    Background = this._resolveResourceBrush("FooterBackground", Brushes.Black),
                });
            }
            else
            {
                paragraph.Inlines.Add(new Run(token));
            }

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
        {
            paragraph.Inlines.Add(new Run(text[cursor..]));
        }
    }
}
