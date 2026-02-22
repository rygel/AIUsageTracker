using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Core.Models;
using Moq;
using System.Text;
using System.Text.Json;

namespace AIUsageTracker.Monitor.Tests;

public class ExportServiceTests
{
    private readonly Mock<IUsageDatabase> _mockDatabase;
    private readonly ExportService _service;

    public ExportServiceTests()
    {
        _mockDatabase = new Mock<IUsageDatabase>();
        _service = new ExportService(_mockDatabase.Object);
    }

    [Fact]
    public async Task ExportAsync_Json_ReturnsValidJsonContent()
    {
        // Arrange
        var history = new List<ProviderUsage>
        {
            new ProviderUsage 
            { 
                ProviderId = "test-p", 
                ProviderName = "Test Provider", 
                RequestsUsed = 10, 
                FetchedAt = DateTime.UtcNow 
            }
        };
        _mockDatabase.Setup(d => d.GetHistoryAsync(It.IsAny<int>())).ReturnsAsync(history);

        // Act
        var (content, contentType, fileName) = await _service.ExportAsync("json", 7);

        // Assert
        Assert.Equal("application/json", contentType);
        Assert.EndsWith(".json", fileName);
        
        var json = Encoding.UTF8.GetString(content);
        var deserialized = JsonSerializer.Deserialize<List<ProviderUsage>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        
        Assert.NotNull(deserialized);
        Assert.Single(deserialized);
        Assert.Equal("test-p", deserialized[0].ProviderId);
    }

    [Fact]
    public async Task ExportAsync_Csv_ReturnsValidCsvContent()
    {
        // Arrange
        var history = new List<ProviderUsage>
        {
            new ProviderUsage 
            { 
                ProviderId = "test-p", 
                ProviderName = "Test Provider", 
                RequestsUsed = 10, 
                UsageUnit = "USD",
                PlanType = PlanType.Usage,
                FetchedAt = DateTime.UtcNow 
            }
        };
        _mockDatabase.Setup(d => d.GetHistoryAsync(It.IsAny<int>())).ReturnsAsync(history);

        // Act
        var (content, contentType, fileName) = await _service.ExportAsync("csv", 7);

        // Assert
        Assert.Equal("text/csv", contentType);
        Assert.EndsWith(".csv", fileName);
        
        var csv = Encoding.UTF8.GetString(content);
        var lines = csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        
        Assert.True(lines.Length >= 2);
        Assert.Contains("Time,Provider,Model,Used,Cost,Unit,PlanType", lines[0]);
        Assert.Contains("Test Provider", lines[1]);
        Assert.Contains("10.00", lines[1]); // Invariant F2 format
        Assert.Contains("USD", lines[1]);
    }

    [Fact]
    public async Task ExportAsync_CsvWithDetails_IncludesDetails()
    {
        // Arrange
        var history = new List<ProviderUsage>
        {
            new ProviderUsage 
            { 
                ProviderId = "test-p", 
                ProviderName = "Test Provider", 
                FetchedAt = DateTime.UtcNow,
                Details = new List<ProviderUsageDetail>
                {
                    new ProviderUsageDetail { Name = "Model A", Used = "5.50" }
                }
            }
        };
        _mockDatabase.Setup(d => d.GetHistoryAsync(It.IsAny<int>())).ReturnsAsync(history);

        // Act
        var (content, _, _) = await _service.ExportAsync("csv", 1);

        // Assert
        var csv = Encoding.UTF8.GetString(content);
        Assert.Contains("Model A", csv);
        Assert.Contains("5.50", csv);
    }
}


