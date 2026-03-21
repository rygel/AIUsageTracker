// <copyright file="WpfProviderIconService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace AIUsageTracker.UI.Slim.Services;

/// <summary>
/// Loads SVG provider icons from disk and returns them as WPF <see cref="FrameworkElement"/> instances.
/// Falls back to a coloured initial badge when no SVG asset exists or loading fails.
/// Caches successfully loaded <see cref="ImageSource"/> objects by canonical provider ID.
/// </summary>
internal sealed class WpfProviderIconService
{
    private static readonly Dictionary<string, Brush> BadgeBrushCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;
    private readonly Func<string, SolidColorBrush, SolidColorBrush> _resolveResourceBrush;

    public WpfProviderIconService(
        ILogger logger,
        Func<string, SolidColorBrush, SolidColorBrush> resolveResourceBrush)
    {
        this._logger = logger;
        this._resolveResourceBrush = resolveResourceBrush;
    }

    /// <summary>
    /// Returns a 16×16 provider icon for <paramref name="providerId"/>.
    /// First tries an SVG asset; falls back to a coloured initial badge.
    /// </summary>
    /// <returns></returns>
    public FrameworkElement CreateIcon(string providerId)
    {
        var canonicalId = ProviderMetadataCatalog.GetCanonicalProviderId(providerId);

        if (this._cache.TryGetValue(canonicalId, out var cached))
        {
            return MakeImage(cached);
        }

        var filename = ProviderMetadataCatalog.GetIconAssetName(providerId);
        var svgPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Assets",
            "ProviderLogos",
            $"{filename}.svg");

        if (System.IO.File.Exists(svgPath))
        {
            try
            {
                var settings = new WpfDrawingSettings
                {
                    IncludeRuntime = true,
                    TextAsGeometry = true,
                };
                var reader = new FileSvgReader(settings);
                var drawing = reader.Read(svgPath);
                if (drawing != null)
                {
                    var imageSource = new DrawingImage(drawing);
                    imageSource.Freeze();
                    this._cache[canonicalId] = imageSource;
                    return MakeImage(imageSource);
                }
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(
                    ex,
                    "Failed to load SVG icon for provider '{ProviderId}' at '{SvgPath}'. Falling back to initial badge.",
                    canonicalId,
                    svgPath);
            }
        }

        return this.CreateFallbackBadge(canonicalId);
    }

    private FrameworkElement CreateFallbackBadge(string canonicalId)
    {
        var (color, initial) = GetBadge(
            canonicalId,
            this._resolveResourceBrush("SecondaryText", Brushes.Gray));

        var grid = new Grid { Width = 16, Height = 16 };

        grid.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            Background = color,
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        grid.Children.Add(new TextBlock
        {
            Text = initial,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });

        return grid;
    }

    private static Image MakeImage(ImageSource source) =>
        new()
        {
            Source = source,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
        };

    internal static (Brush Color, string Initial) GetBadge(string providerId, Brush defaultBrush)
    {
        var canonicalProviderId = ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
        return TryGetBadgeDefinition(canonicalProviderId, out var badgeColor, out var badgeInitial)
            ? (badgeColor, badgeInitial)
            : (defaultBrush, canonicalProviderId[..Math.Min(2, canonicalProviderId.Length)].ToUpperInvariant());
    }

    private static bool TryGetBadgeDefinition(string providerId, out Brush color, out string initial)
    {
        color = null!;
        initial = string.Empty;

        if (!ProviderMetadataCatalog.TryGetBadgeDefinition(providerId, out var colorHex, out var badgeInitial))
        {
            return false;
        }

        color = GetOrCreateBrush(colorHex);
        initial = badgeInitial;
        return true;
    }

    private static Brush GetOrCreateBrush(string colorHex)
    {
        if (BadgeBrushCache.TryGetValue(colorHex, out var brush))
        {
            return brush;
        }

        brush = (SolidColorBrush)new BrushConverter().ConvertFrom(colorHex)!;
        brush.Freeze();
        BadgeBrushCache[colorHex] = brush;
        return brush;
    }
}
