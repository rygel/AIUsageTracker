// <copyright file="ResetTimeParserTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Utilities;

namespace AIUsageTracker.Tests.Core.Utilities;

public class ResetTimeParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void FromUnixSeconds_InvalidInput_ReturnsNull(long? value)
    {
        Assert.Null(ResetTimeParser.FromUnixSeconds(value));
    }

    [Fact]
    public void FromUnixSeconds_ValidTimestamp_ReturnsLocalDateTime()
    {
        var result = ResetTimeParser.FromUnixSeconds(1700000000);

        Assert.NotNull(result);
        Assert.NotEqual(default, result.Value);
    }

    [Fact]
    public void FromUnixMilliseconds_ValidTimestamp_ReturnsLocalDateTime()
    {
        Assert.NotNull(ResetTimeParser.FromUnixMilliseconds(1700000000000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-10.0)]
    public void FromSecondsFromNow_InvalidInput_ReturnsNull(double? value)
    {
        Assert.Null(ResetTimeParser.FromSecondsFromNow(value));
    }

    [Fact]
    public void FromSecondsFromNow_PositiveValue_ReturnsFutureTime()
    {
        var before = DateTime.UtcNow.Add(TimeSpan.FromSeconds(1));

        var result = ResetTimeParser.FromSecondsFromNow(3600);

        Assert.NotNull(result);
        Assert.True(result.Value > before);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    public void FromIso8601_InvalidInput_ReturnsNull(string? value)
    {
        Assert.Null(ResetTimeParser.FromIso8601(value));
    }

    [Fact]
    public void FromIso8601_ValidUtcString_ConvertsToLocal()
    {
        var result = ResetTimeParser.FromIso8601("2026-04-28T12:00:00Z");

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Local, result.Value.Kind);
    }

    [Fact]
    public void Parse_Iso8601AndCommonFormats_ReturnsDateTime()
    {
        Assert.NotNull(ResetTimeParser.Parse("2026-04-28T12:00:00Z"));
        Assert.NotNull(ResetTimeParser.Parse("2026-04-28 10:30:00"));
        Assert.Null(ResetTimeParser.Parse(null));
    }

    [Fact]
    public void FromJsonElement_HandlesNumbersAndStrings()
    {
        using var secondsDoc = JsonDocument.Parse("1700000000");
        Assert.NotNull(ResetTimeParser.FromJsonElement(secondsDoc.RootElement));

        using var millisDoc = JsonDocument.Parse("1700000000000");
        Assert.NotNull(ResetTimeParser.FromJsonElement(millisDoc.RootElement));

        using var stringDoc = JsonDocument.Parse("\"2026-04-28T12:00:00Z\"");
        Assert.NotNull(ResetTimeParser.FromJsonElement(stringDoc.RootElement));
    }

    [Fact]
    public void FormatForDisplay_Null_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, ResetTimeParser.FormatForDisplay(null));
    }

    [Fact]
    public void FormatForDisplay_ValidDate_ContainsResetLabel()
    {
        var result = ResetTimeParser.FormatForDisplay(new DateTime(2026, 4, 28, 14, 30, 0, DateTimeKind.Local));

        Assert.Contains("Resets", result, StringComparison.Ordinal);
        Assert.Contains("Apr", result, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSoonest_MixedValues_ReturnsEarliest()
    {
        var earlier = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Null(ResetTimeParser.GetSoonest(null, null));
        Assert.Equal(earlier, ResetTimeParser.GetSoonest(later, null, earlier));
    }

    [Fact]
    public void IsFuture_NullMeansAlwaysActive()
    {
        Assert.True(ResetTimeParser.IsFuture(null));
        Assert.True(ResetTimeParser.IsFuture(DateTime.UtcNow.AddHours(1)));
        Assert.False(ResetTimeParser.IsFuture(DateTime.UtcNow.AddHours(-1)));
    }

    [Fact]
    public void GetTimeRemaining_FutureAndPast()
    {
        Assert.Equal(TimeSpan.Zero, ResetTimeParser.GetTimeRemaining(null));
        Assert.Equal(TimeSpan.Zero, ResetTimeParser.GetTimeRemaining(DateTime.UtcNow.AddHours(-1)));

        var remaining = ResetTimeParser.GetTimeRemaining(DateTime.UtcNow.AddHours(2), DateTime.UtcNow);
        Assert.True(remaining > TimeSpan.FromHours(1.9));
    }
}
