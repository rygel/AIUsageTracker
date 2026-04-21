// <copyright file="WpfProviderIconService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.IO;
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
/// Returns an explicit missing-icon marker when no SVG asset exists or loading fails.
/// Caches successfully loaded <see cref="ImageSource"/> objects by provider ID.
/// </summary>
internal sealed class WpfProviderIconService
{
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
    /// First tries an SVG asset; otherwise returns an explicit missing-icon marker.
    /// </summary>
    /// <returns></returns>
    public FrameworkElement CreateIcon(string providerId)
    {
        var resolvedProviderId = providerId ?? string.Empty;

        if (this._cache.TryGetValue(resolvedProviderId, out var cached))
        {
            return this.MakeIconElement(cached);
        }

        var filename = ProviderMetadataCatalog.GetIconAssetName(resolvedProviderId);
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
                    this._cache[resolvedProviderId] = imageSource;
                    return this.MakeIconElement(imageSource);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
            {
                this._logger.LogWarning(
                    ex,
                    "Failed to load SVG icon for provider '{ProviderId}' at '{SvgPath}'. Using missing-icon marker.",
                    resolvedProviderId,
                    svgPath);
            }
        }
        else
        {
            this._logger.LogWarning(
                "SVG icon asset missing for provider '{ProviderId}' at '{SvgPath}'. Using missing-icon marker.",
                resolvedProviderId,
                svgPath);
        }

        return CreateMissingIconMarker();
    }

    private static FrameworkElement CreateMissingIconMarker()
    {
        var grid = new Grid { Width = 16, Height = 16 };

        grid.Children.Add(new Border
        {
            Width = 16,
            Height = 16,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.OrangeRed,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        grid.Children.Add(new TextBlock
        {
            Text = "!",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.OrangeRed,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });

        return grid;
    }

    private FrameworkElement MakeIconElement(ImageSource source)
    {
        var image = new Image
        {
            Source = source,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Stretch = Stretch.Uniform,
        };

        // On dark themes, dark SVG icons are invisible. Add a subtle light tile
        // behind the icon so dark marks remain visible on dark cards.
        var bg = this._resolveResourceBrush("Background", Brushes.White);
        if (bg is SolidColorBrush solid && IsDarkColor(solid.Color))
        {
            var backdropBrush = CreateIconBackdropBrush(this._resolveResourceBrush("PrimaryText", Brushes.White));
            var viewbox = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                Child = image,
            };
            return new Border
            {
                Width = 16,
                Height = 16,
                Background = backdropBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(1),
                Child = viewbox,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
        }

        return image;
    }

    private static SolidColorBrush CreateIconBackdropBrush(Brush baseBrush)
    {
        var color = baseBrush is SolidColorBrush solid ? solid.Color : Colors.White;
        var backdrop = new SolidColorBrush(Color.FromArgb(64, color.R, color.G, color.B));
        backdrop.Freeze();
        return backdrop;
    }

    private static bool IsDarkColor(Color c)
    {
        return ((0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B)) < 128;
    }
}
