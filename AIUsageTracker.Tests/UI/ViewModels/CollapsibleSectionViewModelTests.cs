// <copyright file="CollapsibleSectionViewModelTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.UI.Slim.ViewModels;

namespace AIUsageTracker.Tests.UI.ViewModels;

/// <summary>
/// Tests for the CollapsibleSectionViewModel presentation logic.
/// </summary>
public class CollapsibleSectionViewModelTests
{
    private static AppPreferences CreateDefaultPreferences() => new()
    {
        IsPlansAndQuotasCollapsed = false,
        IsPayAsYouGoCollapsed = false,
    };

    [Fact]
    public void Constructor_InitializesTitle()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new CollapsibleSectionViewModel(
            "Plans & Quotas",
            isQuotaSection: true,
            prefs);

        // Assert
        Assert.Equal("Plans & Quotas", viewModel.Title);
    }

    [Fact]
    public void DisplayTitle_ReturnsUppercaseTitle_ForGroupHeader()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new CollapsibleSectionViewModel(
            "Plans & Quotas",
            isQuotaSection: true,
            prefs);

        // Assert
        Assert.Equal("PLANS & QUOTAS", viewModel.DisplayTitle);
    }

    [Fact]
    public void IsExpanded_InitializedFromPreferences_QuotaSection()
    {
        // Arrange
        var prefs = new AppPreferences { IsPlansAndQuotasCollapsed = true };

        // Act
        var viewModel = new CollapsibleSectionViewModel(
            "Plans & Quotas",
            isQuotaSection: true,
            prefs);

        // Assert - collapsed in preferences means NOT expanded
        Assert.False(viewModel.IsExpanded);
    }

    [Fact]
    public void IsExpanded_InitializedFromPreferences_PaygoSection()
    {
        // Arrange
        var prefs = new AppPreferences { IsPayAsYouGoCollapsed = true };

        // Act
        var viewModel = new CollapsibleSectionViewModel(
            "Pay As You Go",
            isQuotaSection: false,
            prefs);

        // Assert - collapsed in preferences means NOT expanded
        Assert.False(viewModel.IsExpanded);
    }

    [Fact]
    public void ToggleSymbol_ReturnsDownArrow_WhenExpanded()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();
        var viewModel = new CollapsibleSectionViewModel(
            "Test Section",
            isQuotaSection: true,
            prefs);

        // Ensure expanded
        viewModel.IsExpanded = true;

        // Assert - down arrow when expanded
        Assert.Equal("\u25BC", viewModel.ToggleSymbol);
    }

    [Fact]
    public void ToggleSymbol_ReturnsRightArrow_WhenCollapsed()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();
        var viewModel = new CollapsibleSectionViewModel(
            "Test Section",
            isQuotaSection: true,
            prefs);

        // Collapse
        viewModel.IsExpanded = false;

        // Assert - right arrow when collapsed
        Assert.Equal("\u25B6", viewModel.ToggleSymbol);
    }

    [Fact]
    public async Task ToggleExpandedCommand_TogglesIsExpandedAsync()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();
        var viewModel = new CollapsibleSectionViewModel(
            "Test Section",
            isQuotaSection: true,
            prefs);

        var initialState = viewModel.IsExpanded;

        // Act
        await viewModel.ToggleExpandedCommand.ExecuteAsync(null);

        // Assert
        Assert.NotEqual(initialState, viewModel.IsExpanded);
    }

    [Fact]
    public async Task ToggleExpandedCommand_UpdatesPreferences_QuotaSectionAsync()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();
        var viewModel = new CollapsibleSectionViewModel(
            "Plans & Quotas",
            isQuotaSection: true,
            prefs);

        // Act - toggle to collapsed
        viewModel.IsExpanded = true; // start expanded
        await viewModel.ToggleExpandedCommand.ExecuteAsync(null);

        // Assert - preferences should reflect collapsed state
        Assert.True(prefs.IsPlansAndQuotasCollapsed);
    }

    [Fact]
    public async Task ToggleExpandedCommand_UpdatesPreferences_PaygoSectionAsync()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();
        var viewModel = new CollapsibleSectionViewModel(
            "Pay As You Go",
            isQuotaSection: false,
            prefs);

        // Act - toggle to collapsed
        viewModel.IsExpanded = true; // start expanded
        await viewModel.ToggleExpandedCommand.ExecuteAsync(null);

        // Assert - preferences should reflect collapsed state
        Assert.True(prefs.IsPayAsYouGoCollapsed);
    }

    [Fact]
    public async Task ToggleExpandedCommand_CallsSavePreferencesCallbackAsync()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();
        var saveCallCount = 0;
        Func<Task> saveCallback = () =>
        {
            saveCallCount++;
            return Task.CompletedTask;
        };

        var viewModel = new CollapsibleSectionViewModel(
            "Test Section",
            isQuotaSection: true,
            prefs,
            saveCallback);

        // Act
        await viewModel.ToggleExpandedCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(1, saveCallCount);
    }

    [Fact]
    public void SectionKey_ReturnsCorrectKey_ForQuotaSection()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new CollapsibleSectionViewModel(
            "Plans & Quotas",
            isQuotaSection: true,
            prefs);

        // Assert
        Assert.Equal("PlansAndQuotas", viewModel.SectionKey);
    }

    [Fact]
    public void SectionKey_ReturnsCorrectKey_ForPaygoSection()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new CollapsibleSectionViewModel(
            "Pay As You Go",
            isQuotaSection: false,
            prefs);

        // Assert
        Assert.Equal("PayAsYouGo", viewModel.SectionKey);
    }

    [Fact]
    public void Items_IsInitializedAsEmptyCollection()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();

        // Act
        var viewModel = new CollapsibleSectionViewModel(
            "Test Section",
            isQuotaSection: true,
            prefs);

        // Assert
        Assert.NotNull(viewModel.Items);
        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public void OnIsExpandedChanged_NotifiesToggleSymbol()
    {
        // Arrange
        var prefs = CreateDefaultPreferences();
        var viewModel = new CollapsibleSectionViewModel(
            "Test Section",
            isQuotaSection: true,
            prefs);

        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != null)
            {
                changedProperties.Add(e.PropertyName);
            }
        };

        // Act
        viewModel.IsExpanded = !viewModel.IsExpanded;

        // Assert
        Assert.Contains("ToggleSymbol", changedProperties);
    }
}
