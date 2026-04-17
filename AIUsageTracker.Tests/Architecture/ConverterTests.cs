// <copyright file="ConverterTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Windows.Media;
using AIUsageTracker.UI.Slim.Converters;

namespace AIUsageTracker.Tests.Architecture;

/// <summary>
/// Tests for WPF value converters.
/// </summary>
public class ConverterTests
{
    [Theory]
    [InlineData(50, 60, 80, false, "Green")] // Pay-as-you-go: 50% used → green
    [InlineData(65, 60, 80, false, "Yellow")] // Pay-as-you-go: 65% used → yellow
    [InlineData(85, 60, 80, false, "Red")] // Pay-as-you-go: 85% used → red
    [InlineData(50, 60, 80, true, "Green")] // Quota: 50% used → green (50% remaining — well within budget)
    [InlineData(65, 60, 80, true, "Yellow")] // Quota: 65% used → yellow (35% remaining — approaching limit)
    [InlineData(85, 60, 80, true, "Red")] // Quota: 85% used → red  (15% remaining — critical)
    public void PercentageToColorConverter_ReturnsCorrectColor(
        double percentage, int yellow, int red, bool isQuota, string expectedColorName)
    {
        var converter = new PercentageToColorConverter();
        var result = converter.Convert(
            new object[] { percentage, yellow, red, isQuota },
            typeof(Brush),
            null!,
            CultureInfo.InvariantCulture);

        Assert.NotNull(result);
        Assert.IsType<SolidColorBrush>(result, false);

        var brush = (SolidColorBrush)result;
        var actualColorName = GetColorName(brush.Color);

        Assert.Equal(expectedColorName, actualColorName);
    }

    [Theory]
    [InlineData(true, false, "Visible")]
    [InlineData(false, false, "Collapsed")]
    [InlineData(true, true, "Collapsed")] // Inverted
    [InlineData(false, true, "Visible")] // Inverted
    public void BoolToVisibilityConverter_ReturnsCorrectVisibility(
        bool input, bool invert, string expectedVisibility)
    {
        var converter = new BoolToVisibilityConverter { Invert = invert };
        var result = converter.Convert(input, typeof(System.Windows.Visibility), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expectedVisibility, result.ToString());
    }

    [Fact]
    public void PrivacyMaskConverter_MasksValueWhenPrivacyEnabled()
    {
        var converter = new PrivacyMaskConverter();
        var result = converter.Convert(
            new object[] { "SensitiveData", true },
            typeof(string),
            null!,
            CultureInfo.InvariantCulture);

        Assert.Equal("****", result);
    }

    [Fact]
    public void PrivacyMaskConverter_ReturnsOriginalWhenPrivacyDisabled()
    {
        var converter = new PrivacyMaskConverter();
        var result = converter.Convert(
            new object[] { "SensitiveData", false },
            typeof(string),
            null!,
            CultureInfo.InvariantCulture);

        Assert.Equal("SensitiveData", result);
    }

    [Fact]
    public void RelativeTimeConverter_FormatsTimeCorrectly()
    {
        var converter = new RelativeTimeConverter();

        // Use UtcNow to avoid DST offset shifts making durations shrink or grow.
        // Test future time - about 5 minutes (use a generous range to avoid timing issues)
        var fiveMinutes = DateTime.UtcNow.AddMinutes(5).AddSeconds(30);
        var result = converter.Convert(fiveMinutes, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.True(
            string.Equals(result?.ToString(), "5m", StringComparison.Ordinal) || string.Equals(result?.ToString(), "6m", StringComparison.Ordinal),
            $"Expected 5m or 6m, got {result}");

        // Test future time - 2 hours
        var twoHours = DateTime.UtcNow.AddHours(2).AddMinutes(1);
        result = converter.Convert(twoHours, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.StartsWith("2h", result?.ToString() ?? string.Empty, StringComparison.Ordinal);

        // Test future time - 3 days
        var threeDays = DateTime.UtcNow.AddDays(3).AddHours(1);
        result = converter.Convert(threeDays, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.StartsWith("3d", result?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void PercentageToWidthConverter_ReturnsValidGridLength()
    {
        var converter = new PercentageToWidthConverter();

        var result = converter.Convert(50.0, typeof(System.Windows.GridLength), null!, CultureInfo.InvariantCulture);
        Assert.IsType<System.Windows.GridLength>(result);

        var gridLength = (System.Windows.GridLength)result;
        Assert.True(gridLength.IsStar);
        Assert.Equal(50.0, gridLength.Value);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(50.0, 50.0)]
    [InlineData(100.0, 100.0)]
    [InlineData(-10.0, 0.0)] // Should clamp to 0
    [InlineData(150.0, 100.0)] // Should clamp to 100
    public void PercentageToWidthConverter_ClampsValues(double input, double expectedValue)
    {
        var converter = new PercentageToWidthConverter();
        var result = converter.Convert(input, typeof(System.Windows.GridLength), null!, CultureInfo.InvariantCulture);

        Assert.IsType<System.Windows.GridLength>(result);
        var gridLength = (System.Windows.GridLength)result;
        Assert.Equal(expectedValue, gridLength.Value);
    }

    [Theory]
    [InlineData(null, "Collapsed")]
    [InlineData("", "Collapsed")]
    [InlineData("  ", "Collapsed")]
    [InlineData("value", "Visible")]
    public void NullToVisibilityConverter_ReturnsCorrectVisibility(object? input, string expectedVisibility)
    {
        var converter = new NullToVisibilityConverter();
        var result = converter.Convert(input!, typeof(System.Windows.Visibility), null!, CultureInfo.InvariantCulture);

        Assert.Equal(expectedVisibility, result.ToString());
    }

    private static string GetColorName(Color color)
    {
        // Check for known progress bar colors
        if (color.R >= 70 && color.R <= 80 && color.G >= 170 && color.G <= 180 && color.B >= 75 && color.B <= 85)
        {
            return "Green";
        }

        if (color.R >= 250 && color.G >= 190 && color.G <= 200 && color.B <= 10)
        {
            return "Yellow";
        }

        if (color.R >= 240 && color.G <= 70 && color.B <= 60)
        {
            return "Red";
        }

        return $"Unknown({color.R},{color.G},{color.B})";
    }
}
