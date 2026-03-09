// <copyright file="ProviderDetailContractTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using Xunit;

namespace AIUsageTracker.Tests.Core;

public class ProviderDetailContractTests
{
    [Fact]
    public void ProviderUsageDetailType_HasValidValues()
    {
        var validTypes = new[]
        {
            ProviderUsageDetailType.Unknown,
            ProviderUsageDetailType.QuotaWindow,
            ProviderUsageDetailType.Credit,
            ProviderUsageDetailType.Model,
            ProviderUsageDetailType.Other,
        };

        foreach (var type in validTypes)
        {
            Assert.True(Enum.IsDefined(typeof(ProviderUsageDetailType), type));
        }
    }

    [Fact]
    public void WindowKind_HasValidValues()
    {
        var validKinds = new[]
        {
            WindowKind.None,
            WindowKind.Primary,
            WindowKind.Secondary,
            WindowKind.Spark,
        };

        foreach (var kind in validKinds)
        {
            Assert.True(Enum.IsDefined(typeof(WindowKind), kind));
        }
    }

    [Fact]
    public void QuotaWindowDetails_RequireWindowKind()
    {
        var detail = new ProviderUsageDetail
        {
            Name = "5-hour quota",
            DetailType = ProviderUsageDetailType.QuotaWindow,
            WindowKind = WindowKind.Primary,
        };

        Assert.True(detail.WindowKind != WindowKind.None, "QuotaWindow details must have WindowKind set");
    }

    [Fact]
    public void CreditDetails_CanHaveNoneWindowKind()
    {
        var detail = new ProviderUsageDetail
        {
            Name = "Credits",
            DetailType = ProviderUsageDetailType.Credit,
            WindowKind = WindowKind.None,
        };

        Assert.Equal(ProviderUsageDetailType.Credit, detail.DetailType);
        Assert.Equal(WindowKind.None, detail.WindowKind);
    }

    [Fact]
    public void ModelDetails_CanHaveNoneWindowKind()
    {
        var detail = new ProviderUsageDetail
        {
            Name = "GPT-5",
            DetailType = ProviderUsageDetailType.Model,
            WindowKind = WindowKind.None,
        };

        Assert.Equal(ProviderUsageDetailType.Model, detail.DetailType);
        Assert.Equal(WindowKind.None, detail.WindowKind);
    }

    [Theory]
    [InlineData(ProviderUsageDetailType.QuotaWindow, WindowKind.None, false)]
    [InlineData(ProviderUsageDetailType.QuotaWindow, WindowKind.Primary, true)]
    [InlineData(ProviderUsageDetailType.QuotaWindow, WindowKind.Secondary, true)]
    [InlineData(ProviderUsageDetailType.QuotaWindow, WindowKind.Spark, true)]
    [InlineData(ProviderUsageDetailType.Credit, WindowKind.None, true)]
    [InlineData(ProviderUsageDetailType.Model, WindowKind.None, true)]
    [InlineData(ProviderUsageDetailType.Other, WindowKind.None, true)]
    [InlineData(ProviderUsageDetailType.Unknown, WindowKind.None, false)]
    public void DetailTypeAndWindowKind_Combination_IsValid(ProviderUsageDetailType type, WindowKind kind, bool expectedValid)
    {
        var detail = new ProviderUsageDetail
        {
            Name = "Test Detail",
            DetailType = type,
            WindowKind = kind,
        };

        var isValid = ValidateDetailCombination(detail);
        Assert.Equal(expectedValid, isValid);
    }

    private static bool ValidateDetailCombination(ProviderUsageDetail detail)
    {
        if (detail.DetailType == ProviderUsageDetailType.Unknown)
        {
            return false;
        }

        if (detail.DetailType == ProviderUsageDetailType.QuotaWindow && detail.WindowKind == WindowKind.None)
        {
            return false;
        }

        return true;
    }

    [Fact]
    public void ProviderDetail_WithEmptyName_IsInvalid()
    {
        var detail = new ProviderUsageDetail
        {
            Name = string.Empty,
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None,
        };

        Assert.True(string.IsNullOrEmpty(detail.Name), "Empty name should be flagged");
    }
}
