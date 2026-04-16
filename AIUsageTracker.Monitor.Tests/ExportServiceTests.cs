// <copyright file="ExportServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class ExportServiceTests
{
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly Mock<IUsageDatabase> _mockDatabase;
    private readonly ExportService _service;

    public ExportServiceTests()
    {
        this._mockDatabase = new Mock<IUsageDatabase>();
        this._service = new ExportService(this._mockDatabase.Object);
    }

    [Fact]
    public async Task ExportAsync_Json_ReturnsValidJsonContentAsync()
    {
        // Arrange
        var history = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test-p",
                ProviderName = "Test Provider",
                RequestsUsed = 10,
                FetchedAt = DateTime.UtcNow,
            },
        };
        this._mockDatabase.Setup(d => d.GetHistoryAsync(It.IsAny<int>())).ReturnsAsync(history);

        // Act
        var (content, contentType, fileName) = await this._service.ExportAsync("json", 7);

        // Assert
        Assert.Equal("application/json", contentType);
        Assert.EndsWith(".json", fileName, StringComparison.Ordinal);

        var json = Encoding.UTF8.GetString(content);
        var deserialized = JsonSerializer.Deserialize<List<ProviderUsage>>(json, CaseInsensitiveOptions);

        Assert.NotNull(deserialized);
        Assert.Single(deserialized);
        Assert.Equal("test-p", deserialized[0].ProviderId);
    }

    [Fact]
    public async Task ExportAsync_Csv_ReturnsValidCsvContentAsync()
    {
        // Arrange
        var history = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test-p",
                ProviderName = "Test Provider",
                RequestsUsed = 10,
                IsCurrencyUsage = true,
                PlanType = PlanType.Usage,
                FetchedAt = DateTime.UtcNow,
            },
        };
        this._mockDatabase.Setup(d => d.GetHistoryAsync(It.IsAny<int>())).ReturnsAsync(history);

        // Act
        var (content, contentType, fileName) = await this._service.ExportAsync("csv", 7);

        // Assert
        Assert.Equal("text/csv", contentType);
        Assert.EndsWith(".csv", fileName, StringComparison.Ordinal);

        var csv = Encoding.UTF8.GetString(content);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.True(lines.Length >= 2);
        Assert.Contains("Time,Provider,Model,Used,Cost,PlanType", lines[0], StringComparison.Ordinal);
        Assert.Contains("Test Provider", lines[1], StringComparison.Ordinal);
        Assert.Contains("10.00", lines[1], StringComparison.Ordinal); // Invariant F2 format
    }

    [Fact]
    public async Task ExportAsync_CsvWithFlatCards_IncludesProviderDataAsync()
    {
        // Arrange — flat cards replace Details; each card row appears as a separate CSV entry
        var history = new List<ProviderUsage>
        {
            new ProviderUsage
            {
                ProviderId = "test-p",
                ProviderName = "Test Provider",
                RequestsUsed = 5.50,
                FetchedAt = DateTime.UtcNow,
            },
        };
        this._mockDatabase.Setup(d => d.GetHistoryAsync(It.IsAny<int>())).ReturnsAsync(history);

        // Act
        var (content, _, _) = await this._service.ExportAsync("csv", 1);

        // Assert
        var csv = Encoding.UTF8.GetString(content);
        Assert.Contains("Test Provider", csv, StringComparison.Ordinal);
        Assert.Contains("5.50", csv, StringComparison.Ordinal);
    }
}
