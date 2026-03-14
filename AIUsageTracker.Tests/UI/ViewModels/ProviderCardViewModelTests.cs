// <copyright file="ProviderCardViewModelTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.ViewModels;

namespace AIUsageTracker.Tests.UI.ViewModels;

/// <summary>
/// Tests for the ProviderCardViewModel presentation logic.
/// </summary>
public class ProviderCardViewModelTests
{
    private static AppPreferences CreateDefaultPreferences() => new()
    {
        ColorThresholdYellow = 60,
        ColorThresholdRed = 80,
        ShowUsedPercentages = false,
    };

    [Fact]
    public void Constructor_InitializesProperties_FromUsage()
    {
        // Arrange
        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
            AccountName = "test@example.com",
            IsQuotaBased = true,
        };
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        // Assert
        Assert.Equal("test-provider", viewModel.ProviderId);
        Assert.True(viewModel.IsQuotaBased);
        Assert.False(viewModel.IsPrivacyMode);
        Assert.Equal(60, viewModel.YellowThreshold);
        Assert.Equal(80, viewModel.RedThreshold);
    }

    [Fact]
    public void AccountDisplay_ReturnsAccountName_WhenNotInPrivacyMode()
    {
        // Arrange - use a provider that supports account identity
        var usage = new ProviderUsage
        {
            ProviderId = "github-copilot",
            AccountName = "user@example.com",
        };
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        // Assert
        Assert.Contains("user@example.com", viewModel.AccountDisplay);
    }

    [Fact]
    public void AccountDisplay_ReturnsMaskedValue_WhenInPrivacyMode()
    {
        // Arrange - use a provider that supports account identity
        var usage = new ProviderUsage
        {
            ProviderId = "github-copilot",
            AccountName = "user@example.com",
        };
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: true);

        // Assert
        Assert.Equal("****", viewModel.AccountDisplay);
    }

    [Fact]
    public void HasAccountName_ReturnsTrue_WhenAccountNameIsSet()
    {
        // Arrange
        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
            AccountName = "user@example.com",
        };
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        // Assert
        Assert.True(viewModel.HasAccountName);
    }

    [Fact]
    public void HasAccountName_ReturnsFalse_WhenAccountNameIsEmpty()
    {
        // Arrange
        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
            AccountName = string.Empty,
        };
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        // Assert
        Assert.False(viewModel.HasAccountName);
    }

    [Fact]
    public void IsPrivacyMode_PropertyChanged_UpdatesAccountDisplay()
    {
        // Arrange - use a provider that supports account identity
        var usage = new ProviderUsage
        {
            ProviderId = "github-copilot",
            AccountName = "user@example.com",
        };
        var prefs = CreateDefaultPreferences();
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        var propertyChangedCount = 0;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(viewModel.AccountDisplay), StringComparison.Ordinal))
            {
                propertyChangedCount++;
            }
        };

        // Act
        viewModel.IsPrivacyMode = true;

        // Assert
        Assert.Equal("****", viewModel.AccountDisplay);
        Assert.Equal(1, propertyChangedCount);
    }

    [Fact]
    public void ShowUsedPercentages_PropertyChanged_UpdatesProgressPercentage()
    {
        // Arrange
        var detail = new ProviderUsageDetail { Name = "Tokens" };
        detail.SetPercentageValue(50, PercentageValueSemantic.Used);

        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
            IsQuotaBased = true,
            Details = new List<ProviderUsageDetail> { detail },
        };
        var prefs = CreateDefaultPreferences();
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        var propertyChangedCount = 0;
        viewModel.PropertyChanged += (s, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(viewModel.ProgressPercentage), StringComparison.Ordinal))
            {
                propertyChangedCount++;
            }
        };

        // Act
        viewModel.ShowUsedPercentages = true;

        // Assert
        Assert.Equal(1, propertyChangedCount);
    }

    [Fact]
    public void ProgressPercentage_ReturnsUsedPercent_WhenShowUsedPercentagesIsTrue()
    {
        // Arrange
        var prefs = new AppPreferences
        {
            ColorThresholdYellow = 60,
            ColorThresholdRed = 80,
            ShowUsedPercentages = true,
        };
        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
            IsQuotaBased = true,
        };

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        // Assert - ProgressPercentage should use UsedPercent when ShowUsedPercentages is true
        Assert.Equal(viewModel.UsedPercent, viewModel.ProgressPercentage);
    }

    [Fact]
    public void ProgressPercentage_ReturnsRemainingPercent_WhenShowUsedPercentagesIsFalse()
    {
        // Arrange
        var prefs = new AppPreferences
        {
            ColorThresholdYellow = 60,
            ColorThresholdRed = 80,
            ShowUsedPercentages = false,
        };
        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
            IsQuotaBased = true,
        };

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        // Assert - ProgressPercentage should use RemainingPercent when ShowUsedPercentages is false
        Assert.Equal(viewModel.RemainingPercent, viewModel.ProgressPercentage);
    }

    [Fact]
    public void OnUsageChanged_NotifiesAllProperties()
    {
        // Arrange
        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
        };
        var prefs = CreateDefaultPreferences();
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        // Act - change the usage
        viewModel.Usage = new ProviderUsage
        {
            ProviderId = "new-provider",
            AccountName = "new@example.com",
        };

        // Assert - multiple properties should have been notified
        Assert.Contains("DisplayName", changedProperties);
        Assert.Contains("AccountDisplay", changedProperties);
        Assert.Contains("StatusText", changedProperties);
    }

    [Fact]
    public void Details_PopulatedFromUsageDetails()
    {
        // Arrange
        var detail1 = new ProviderUsageDetail { Name = "Tokens", DetailType = ProviderUsageDetailType.Model };
        detail1.SetPercentageValue(50, PercentageValueSemantic.Used);

        var detail2 = new ProviderUsageDetail { Name = "Requests", DetailType = ProviderUsageDetailType.Model };
        detail2.SetPercentageValue(20, PercentageValueSemantic.Used);

        var usage = new ProviderUsage
        {
            ProviderId = "test-provider",
            IsQuotaBased = true,
            Details = new List<ProviderUsageDetail> { detail1, detail2 },
        };
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new ProviderCardViewModel(usage, prefs, isPrivacyMode: false);

        // Assert - HasDetails depends on displayable details which requires DetailType.Model or Other
        Assert.True(viewModel.HasDetails);
        Assert.Equal(2, viewModel.Details.Count);
    }
}
