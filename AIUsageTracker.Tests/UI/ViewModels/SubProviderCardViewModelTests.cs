// <copyright file="SubProviderCardViewModelTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.ViewModels;

namespace AIUsageTracker.Tests.UI.ViewModels;

/// <summary>
/// Tests for the SubProviderCardViewModel presentation logic.
/// </summary>
public class SubProviderCardViewModelTests
{
    private static ProviderUsageDetail CreateDetail(string name, double percentUsed = 50)
    {
        var detail = new ProviderUsageDetail { Name = name };
        detail.SetPercentageValue(percentUsed, PercentageValueSemantic.Used);
        return detail;
    }

    [Fact]
    public void Constructor_InitializesProperties_FromDetail()
    {
        // Arrange
        var detail = CreateDetail("Token Usage", 50);

        // Act
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: true,
            isPrivacyMode: false,
            showUsedPercentages: false);

        // Assert
        Assert.Equal("Token Usage", viewModel.DisplayName);
        Assert.False(viewModel.IsPrivacyMode);
    }

    [Fact]
    public void DisplayValue_ShowsFormattedUsage()
    {
        // Arrange
        var detail = CreateDetail("Requests", 50);

        // Act
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: true,
            isPrivacyMode: false,
            showUsedPercentages: false);

        // Assert
        Assert.NotEmpty(viewModel.DisplayValue);
    }

    [Fact]
    public void HasProgress_ReturnsTrue_WhenQuotaBasedWithPercentage()
    {
        // Arrange
        var detail = CreateDetail("Tokens", 50);

        // Act
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: true,
            isPrivacyMode: false,
            showUsedPercentages: false);

        // Assert
        Assert.True(viewModel.HasProgress);
    }

    [Fact]
    public void UsedPercent_CalculatesCorrectly()
    {
        // Arrange
        var detail = CreateDetail("Tokens", 25);

        // Act
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: true,
            isPrivacyMode: false,
            showUsedPercentages: false);

        // Assert
        Assert.Equal(25.0, viewModel.UsedPercent, precision: 1);
    }

    [Fact]
    public void OnDetailChanged_NotifiesAllProperties()
    {
        // Arrange
        var detail = CreateDetail("Original", 10);
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: true,
            isPrivacyMode: false,
            showUsedPercentages: false);

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        // Act
        viewModel.Detail = CreateDetail("Updated", 50);

        // Assert
        Assert.Contains("DisplayName", changedProperties);
        Assert.Contains("DisplayValue", changedProperties);
        Assert.Contains("UsedPercent", changedProperties);
    }

    [Fact]
    public void OnIsQuotaBasedChanged_UpdatesPresentation()
    {
        // Arrange
        var detail = CreateDetail("Tokens", 50);
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: false,
            isPrivacyMode: false,
            showUsedPercentages: false);

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        // Act
        viewModel.IsQuotaBased = true;

        // Assert
        Assert.Contains("DisplayValue", changedProperties);
    }

    [Fact]
    public void OnShowUsedPercentagesChanged_UpdatesPresentation()
    {
        // Arrange
        var detail = CreateDetail("Tokens", 50);
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: true,
            isPrivacyMode: false,
            showUsedPercentages: false);

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        // Act
        viewModel.ShowUsedPercentages = true;

        // Assert
        Assert.Contains("DisplayValue", changedProperties);
    }

    [Fact]
    public void NextResetTime_ReturnsValueFromDetail()
    {
        // Arrange
        var resetTime = DateTime.Now.AddHours(2);
        var detail = new ProviderUsageDetail
        {
            Name = "Tokens",
            NextResetTime = resetTime,
        };
        detail.SetPercentageValue(50, PercentageValueSemantic.Used);

        // Act
        var viewModel = new SubProviderCardViewModel(
            detail,
            isQuotaBased: true,
            isPrivacyMode: false,
            showUsedPercentages: false);

        // Assert
        Assert.Equal(resetTime, viewModel.NextResetTime);
    }
}
