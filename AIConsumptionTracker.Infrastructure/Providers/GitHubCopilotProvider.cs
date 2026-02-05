using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Helpers;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class GitHubCopilotProvider : IProviderService
{
    public string ProviderId => "github-copilot";
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubCopilotProvider> _logger;
    private readonly WindowsBrowserCookieService _cookieService;

    public GitHubCopilotProvider(HttpClient httpClient, ILogger<GitHubCopilotProvider> logger, WindowsBrowserCookieService cookieService)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cookieService = cookieService;
    }

    public async Task<ProviderUsage> GetUsageAsync(ProviderConfig config)
    {
        var cookieHeader = await _cookieService.GetCookieHeaderAsync("github.com");

        if (string.IsNullOrEmpty(cookieHeader))
        {
            return new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "GitHub Copilot",
                IsAvailable = false,
                Description = "Not logged in to GitHub in any supported browser"
            };
        }
        
        _logger.LogDebug("Got cookie header for github.com. Fetching Customer ID...");

        try
        {
            // First, we need to get the Customer ID from the billing page
            var customerId = await GetCustomerIdAsync(cookieHeader);
            if (string.IsNullOrEmpty(customerId))
            {
                return new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "GitHub Copilot",
                    IsAvailable = false,
                    Description = "Found cookies but could not find Customer ID"
                };
            }

            // period=3 is usually the current month
            _logger.LogDebug("Fetching usage card and table for Customer ID {id}", customerId);
            var usageCard = await FetchJsonAsync<JsonElement>($"https://github.com/settings/billing/copilot_usage_card?customer_id={customerId}&period=3", cookieHeader);
            var usageTable = await FetchJsonAsync<JsonElement>($"https://github.com/settings/billing/copilot_usage_table?customer_id={customerId}&group=0&period=3", cookieHeader);

            var usage = ParseUsage(usageCard, usageTable);
            usage.ProviderId = ProviderId;
            usage.ProviderName = "GitHub Copilot";
            usage.AuthSource = "Browser Session";
            
            _logger.LogInformation("Successfully parsed GitHub Copilot usage. Used: {Used}, Limit: {Limit}", usage.CostUsed, usage.CostLimit);

            return usage;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub Copilot usage");
            return new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "GitHub Copilot",
                IsAvailable = false,
                Description = "Connection Error"
            };
        }
    }

    private async Task<string> GetCustomerIdAsync(string cookieHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://github.com/settings/billing");
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36");

        var response = await _httpClient.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        var match = Regex.Match(html, "\"customerId\":\\s*(\\d+)");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    private async Task<T> FetchJsonAsync<T>(string url, string cookieHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", cookieHeader);
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/132.0.0.0 Safari/537.36");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new Exception("Empty response");
    }

    private ProviderUsage ParseUsage(JsonElement card, JsonElement table)
    {
        var usage = new ProviderUsage();

        // Parse Usage Card
        double used = GetDouble(card, "netQuantity") + GetDouble(card, "discountQuantity");
        double limit = GetDouble(card, "userPremiumRequestEntitlement");

        usage.IsAvailable = true;
        usage.CostUsed = used;
        usage.CostLimit = limit;
        usage.UsageUnit = "Reqs";
        usage.IsQuotaBased = limit > 0;
        
        if (limit > 0)
        {
            usage.UsagePercentage = Math.Min(100, (used / limit) * 100);
            usage.Description = $"{used:0}/{limit:0} requests used";
        }
        else
        {
             usage.Description = $"{used:0} requests used (Unlimited?)";
        }

        if (card.TryGetProperty("periodEndDate", out var endDateProp) && DateTime.TryParse(endDateProp.GetString(), out var resetTime))
        {
            usage.NextResetTime = resetTime;
        }

        // Parse Usage Table for details
        var details = new List<ProviderUsageDetail>();
        if (table.TryGetProperty("table", out var tableObj) && tableObj.TryGetProperty("rows", out var rows))
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.TryGetProperty("subtable", out var subtable) && subtable.TryGetProperty("rows", out var subRows))
                {
                    foreach (var subRow in subRows.EnumerateArray())
                    {
                        var cells = subRow.GetProperty("cells").EnumerateArray().ToList();
                        if (cells.Count >= 2)
                        {
                            var modelName = cells[0].GetProperty("value").GetString() ?? "Unknown";
                            var modelUsed = cells[1].GetProperty("value").GetString() ?? "0";
                            
                            var existing = details.FirstOrDefault(d => d.Name == modelName);
                            if (existing != null)
                            {
                                if (double.TryParse(existing.Used, out var eUsed) && double.TryParse(modelUsed, out var mUsed))
                                {
                                    existing.Used = (eUsed + mUsed).ToString();
                                }
                            }
                            else
                            {
                                details.Add(new ProviderUsageDetail
                                {
                                    Name = modelName,
                                    Used = modelUsed,
                                    Description = "Requests"
                                });
                            }
                        }
                    }
                }
            }
        }
        
        usage.Details = details.OrderByDescending(d => double.TryParse(d.Used, out var val) ? val : 0).ToList();

        return usage;
    }

    private double GetDouble(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetDouble();
            if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), out var val)) return val;
        }
        return 0;
    }
}
