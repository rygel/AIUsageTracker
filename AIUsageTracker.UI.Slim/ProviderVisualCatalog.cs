// <copyright file="ProviderVisualCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows.Media;

using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderVisualCatalog
{
    private static readonly Dictionary<string, Brush> BadgeBrushCache = new(StringComparer.OrdinalIgnoreCase);

    public static string GetCanonicalProviderId(string providerId)
    {
        return ProviderMetadataCatalog.GetCanonicalProviderId(providerId);
    }

    public static string GetIconAssetName(string providerId)
    {
        return ProviderMetadataCatalog.GetIconAssetName(providerId);
    }

    public static (Brush Color, string Initial) GetFallbackBadge(string providerId, Brush defaultBrush)
    {
        var canonicalProviderId = GetCanonicalProviderId(providerId);
        return TryGetBadgeDefinition(canonicalProviderId, out var badgeColor, out var badgeInitial)
            ? (badgeColor, badgeInitial)
            : (defaultBrush, canonicalProviderId[..Math.Min(2, canonicalProviderId.Length)].ToUpperInvariant());
    }

    private static bool TryGetBadgeDefinition(string providerId, out Brush color, out string initial)
    {
        color = null!;
        initial = string.Empty;

        if (!ProviderMetadataCatalog.TryGetFallbackBadgeDefinition(providerId, out var colorHex, out var badgeInitial))
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
