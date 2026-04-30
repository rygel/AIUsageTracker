// <copyright file="DataExportServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure.Services;

public class DataExportServiceTests
{
    private readonly Mock<IWebDatabaseRepository> _repository;
    private readonly Mock<ILogger<DataExportService>> _logger;
    private readonly DataExportService _service;

    public DataExportServiceTests()
    {
        _repository = new Mock<IWebDatabaseRepository>();
        _logger = new Mock<ILogger<DataExportService>>();
        _service = new DataExportService(_repository.Object, _logger.Object, "nonexistent.db");
    }

    [Fact]
    public async Task ExportHistoryToCsvAsync_WithData_ReturnsCsvWithHeaders()
    {
        var history = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "test-provider",
                ProviderName = "Test Provider",
                RequestsUsed = 10,
                RequestsAvailable = 100,
                UsedPercent = 10.0,
                IsAvailable = true,
                Description = "Active",
                FetchedAt = new DateTime(2026, 4, 28, 12, 0, 0, DateTimeKind.Utc),
            },
        };

        _repository.Setup(r => r.GetAllHistoryForExportAsync(It.IsAny<int>()))
            .ReturnsAsync(history.AsReadOnly());

        var csv = await _service.ExportHistoryToCsvAsync();

        Assert.Contains("provider_id", csv, StringComparison.Ordinal);
        Assert.Contains("test-provider", csv, StringComparison.Ordinal);
        Assert.Contains("Test Provider", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportHistoryToCsvAsync_EmptyData_ReturnsHeadersOnly()
    {
        _repository.Setup(r => r.GetAllHistoryForExportAsync(It.IsAny<int>()))
            .ReturnsAsync(new List<ProviderUsage>().AsReadOnly());

        var csv = await _service.ExportHistoryToCsvAsync();

        Assert.Contains("provider_id", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("test-provider", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportHistoryToCsvAsync_DataWithQuotes_EscapesQuotes()
    {
        var history = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "test",
                ProviderName = "Provider \"quoted\" name",
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsedPercent = 0,
                IsAvailable = false,
                Description = "Status \"with\" quotes",
                FetchedAt = DateTime.UtcNow,
            },
        };

        _repository.Setup(r => r.GetAllHistoryForExportAsync(It.IsAny<int>()))
            .ReturnsAsync(history.AsReadOnly());

        var csv = await _service.ExportHistoryToCsvAsync();

        var escaped = "\"\"" + "with" + "\"\"";
        Assert.Contains(escaped, csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportHistoryToJsonAsync_WithData_ReturnsValidJson()
    {
        var history = new List<ProviderUsage>
        {
            new()
            {
                ProviderId = "test-provider",
                ProviderName = "Test",
                RequestsUsed = 5,
                RequestsAvailable = 50,
                UsedPercent = 10.0,
                IsAvailable = true,
                FetchedAt = DateTime.UtcNow,
            },
        };

        _repository.Setup(r => r.GetAllHistoryForExportAsync(It.IsAny<int>()))
            .ReturnsAsync(history.AsReadOnly());

        var json = await _service.ExportHistoryToJsonAsync();

        Assert.Contains("test-provider", json, StringComparison.Ordinal);
        Assert.StartsWith("[", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportHistoryToJsonAsync_RepositoryThrows_ReturnsEmptyArray()
    {
        _repository.Setup(r => r.GetAllHistoryForExportAsync(It.IsAny<int>()))
            .ThrowsAsync(new IOException("db error"));

        var json = await _service.ExportHistoryToJsonAsync();

        Assert.Equal("[]", json);
    }

    [Fact]
    public async Task CreateDatabaseBackupAsync_NonexistentFile_ReturnsNull()
    {
        var result = await _service.CreateDatabaseBackupAsync();

        Assert.Null(result);
    }
}
