using System.Diagnostics;
using System.Management;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class AntigravityProvider : IProviderService
{
    public string ProviderId => "antigravity";
    private readonly HttpClient _httpClient;
    private readonly ILogger<AntigravityProvider> _logger;

    public AntigravityProvider(ILogger<AntigravityProvider> logger)
    {
        _logger = logger;
        
        // Setup HttpClient with loose SSL validation for localhost
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
        {
             // Only allow localhost self-signed
             return message.RequestUri?.Host == "127.0.0.1";
        };
        _httpClient = new HttpClient(handler);
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        var results = new List<ProviderUsage>();
        
        try
        {
            // 1. Find All Processes
            var processInfos = FindProcessInfos();
            if (!processInfos.Any())
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Antigravity",
                    IsAvailable = false,
                    Description = "Antigravity process not running"
                }};
            }

            foreach (var info in processInfos)
            {
                try
                {
                    var (pid, csrfToken) = info;
                    _logger.LogDebug($"Checking Antigravity process: PID={pid}, CSRF={csrfToken[..8]}...");

                    // 2. Find Port
                    var port = FindListeningPort(pid);
                    
                    // 3. Request
                    var usage = await FetchUsage(port, csrfToken);
                    
                    // Check for duplicates based on AccountName (Email)
                    if (results.Any(r => r.AccountName == usage.AccountName))
                    {
                        continue; // Skip same account running in different window
                    }
                    
                    results.Add(usage);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to check Antigravity PID {info.Pid}");
                }
            }
            
            if (!results.Any())
            {
                 return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Antigravity",
                    IsAvailable = false,
                    Description = "Antigravity process not running or unreachable"
                }};
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Antigravity check failed");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Antigravity",
                IsAvailable = false,
                Description = "Antigravity process not running"
            }};
        }
    }

    private List<(int Pid, string Token)> FindProcessInfos()
    {
        var candidates = new List<(int Pid, string Token)>();

        if (OperatingSystem.IsWindows())
        {
             try 
             {
                // Find all language server processes
                var searcher = new ManagementObjectSearcher("SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE '%language_server_windows%'");
                var collection = searcher.Get();

                foreach (var obj in collection)
                {
                    var cmdLine = obj["CommandLine"]?.ToString();
                    var pidVal = obj["ProcessId"];
                    
                    if (!string.IsNullOrEmpty(cmdLine) && cmdLine.Contains("antigravity"))
                    {
                        var match = Regex.Match(cmdLine, @"--csrf_token[= ]+([a-zA-Z0-9-]+)");
                        if (match.Success)
                        {
                            var pid = Convert.ToInt32(pidVal);
                            candidates.Add((pid, match.Groups[1].Value));
                        }
                    }
                }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Process discovery failed");
             }
        }
        return candidates;
    }

    private int FindListeningPort(int pid)
    {
        // ... (existing netstat logic is fine, it uses the specific PID)
        var startInfo = new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null) return 0;

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var regex = new Regex($@"\s+TCP\s+(?:127\.0\.0\.1|\[::1\]):(\d+)\s+.*LISTENING\s+{pid}");
        
        foreach (var line in lines)
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value);
            }
        }
        
        throw new Exception($"No listening port found for PID {pid}");
    }

    private async Task<ProviderUsage> FetchUsage(int port, string csrfToken)
    {
        var url = $"https://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUserStatus";
        
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Codeium-Csrf-Token", csrfToken);
        request.Headers.Add("Connect-Protocol-Version", "1");
        
        var body = new { metadata = new { ideName = "antigravity", extensionName = "antigravity", ideVersion = "unknown", locale = "en" } };
        request.Content = JsonContent.Create(body);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var responseString = await response.Content.ReadAsStringAsync();
        var data = JsonSerializer.Deserialize<AntigravityResponse>(responseString);
        
        if (data?.UserStatus == null) throw new Exception("Invalid Antigravity response");

        var modelConfigs = data.UserStatus.CascadeModelConfigData?.ClientModelConfigs ?? new List<ClientModelConfig>();
        var modelSorts = data.UserStatus.CascadeModelConfigData?.ClientModelSorts?.FirstOrDefault();
        var masterModelLabels = modelSorts?.Groups?.SelectMany(g => g.ModelLabels ?? new List<string>()).Distinct().ToList() ?? new List<string>();

        var details = new List<ProviderUsageDetail>();
        double minRemaining = 100.0;

        // 1. Check Global Credits
        var planStatus = data.UserStatus.PlanStatus;
        // 1. Check Global Credits - REMOVED per user request

        // Logic removed to hide Flow/Prompt credits
        /* 
        if (planStatus != null) ...
        */

        // 2. Map existing configs for easy lookup
        var configMap = modelConfigs.Where(c => !string.IsNullOrEmpty(c.Label)).ToDictionary(c => c.Label!, c => c);

        // 3. Process all known models (from sorts list) to ensure 0% is shown for exhausted ones
        foreach (var label in masterModelLabels)
        {
            double remainingPct = 0; // Assume exhausted (0% remaining) if missing
            DateTime? itemResetDt = null;
            
            if (configMap.TryGetValue(label, out var config))
            {
                if (config.QuotaInfo?.RemainingFraction.HasValue == true)
                {
                    remainingPct = config.QuotaInfo.RemainingFraction.Value * 100;
                }
                else if (config.QuotaInfo?.TotalRequests.HasValue == true && config.QuotaInfo.TotalRequests > 0)
                {
                    var used = config.QuotaInfo.UsedRequests ?? 0;
                    var remaining = Math.Max(0, config.QuotaInfo.TotalRequests.Value - used);
                    remainingPct = (remaining / (double)config.QuotaInfo.TotalRequests.Value) * 100;
                }
            }

            // Invert for "Used" display
            var detailUsedPct = 100.0 - remainingPct;
            
            string resetStr = "";
            if (!string.IsNullOrEmpty(config?.QuotaInfo?.ResetTime))
            {
                        if (DateTime.TryParse(config.QuotaInfo.ResetTime, out var dt))
                        {
                            var diff = dt.ToLocalTime() - DateTime.Now;
                    if (diff.TotalSeconds > 0)
                    {
                        resetStr = $" (Resets: ({dt.ToLocalTime():MMM dd HH:mm}))";
                        itemResetDt = dt.ToLocalTime();
                    }
                        }
            }

            details.Add(new ProviderUsageDetail
            {
                Name = label,
                Used = $"{detailUsedPct:F0}%",
                Description = resetStr,
                NextResetTime = itemResetDt
            });
            
            minRemaining = Math.Min(minRemaining, remainingPct);
        }

        // Sort but keep [Credits] at the top
        // details are already added in order (Credits first, then models by label order from master list which is usually sorted or fixed)
        // If masterModelLabels is not sorted, we might want to sort models alphabetically.
        
        var modelDetails = details.Where(d => !d.Name.StartsWith("[Credits]")).OrderBy(d => d.Name).ToList();
        var creditDetails = details.Where(d => !d.Name.StartsWith("[Credits]")).ToList(); 
        // Wait, logic above separates them.
        
        // Let's just re-sort the whole list to be safe: Credits first, then Alphabetical Models
        var sortedDetails = details.OrderBy(d => d.Name.StartsWith("[Credits]") ? "0" + d.Name : "1" + d.Name).ToList();

        // Show Max Usage (since minRemaining is smallest remaining fraction)
        var usedPctTotal = 100 - minRemaining;
        
        // Remove globalReset based on user feedback. Group-specific resets would be in Details.

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Antigravity",
            UsagePercentage = usedPctTotal,
            CostUsed = usedPctTotal,
            CostLimit = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PaymentType = PaymentType.Quota,
            Description = $"{usedPctTotal:F1}% Used",

            Details = sortedDetails,
            AccountName = data.UserStatus?.Email ?? ""
        };
    }

    private class AntigravityResponse
    {
        [JsonPropertyName("userStatus")]
        public UserStatus? UserStatus { get; set; }
    }

    private class UserStatus
    {
        [JsonPropertyName("planStatus")]
        public PlanStatus? PlanStatus { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("cascadeModelConfigData")]
        public CascadeModelConfigData? CascadeModelConfigData { get; set; }
    }

    private class PlanStatus
    {
        [JsonPropertyName("availablePromptCredits")]
        public int AvailablePromptCredits { get; set; }
        
        [JsonPropertyName("availableFlowCredits")]
        public int AvailableFlowCredits { get; set; }

        [JsonPropertyName("planInfo")]
        public PlanInfo? PlanInfo { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private class PlanInfo
    {
        [JsonPropertyName("monthlyPromptCredits")]
        public int MonthlyPromptCredits { get; set; }

        [JsonPropertyName("monthlyFlowCredits")]
        public int MonthlyFlowCredits { get; set; }
    }

    private class CascadeModelConfigData
    {
        [JsonPropertyName("clientModelConfigs")]
        public List<ClientModelConfig>? ClientModelConfigs { get; set; }

        [JsonPropertyName("clientModelSorts")]
        public List<ClientModelSort>? ClientModelSorts { get; set; }
    }

    private class ClientModelSort
    {
        [JsonPropertyName("groups")]
        public List<ModelGroup>? Groups { get; set; }
    }

    private class ModelGroup
    {
        [JsonPropertyName("modelLabels")]
        public List<string>? ModelLabels { get; set; }
    }

    private class ClientModelConfig
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }
        
        [JsonPropertyName("quotaInfo")]
        public QuotaInfo? QuotaInfo { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private class QuotaInfo
    {
        [JsonPropertyName("remainingFraction")]
        public double? RemainingFraction { get; set; }
        
        [JsonPropertyName("totalRequests")]
        public int? TotalRequests { get; set; }
        
        [JsonPropertyName("usedRequests")]
        public int? UsedRequests { get; set; }

        [JsonPropertyName("resetTime")]
        public string? ResetTime { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }
}

