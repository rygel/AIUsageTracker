using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

using AIUsageTracker.Infrastructure.Helpers;

namespace AIUsageTracker.Infrastructure.Providers;

    public class AntigravityProvider : IProviderService
{
    public string ProviderId => "antigravity";
    private readonly HttpClient _httpClient;
    private readonly ILogger<AntigravityProvider> _logger;
    private ProviderUsage? _cachedUsage;
    private DateTime _cacheTimestamp;
    private List<(int Pid, string Token, int? Port)>? _cachedProcessInfos;
    private DateTime _lastProcessCheck = DateTime.MinValue;

    public AntigravityProvider(ILogger<AntigravityProvider> logger)
        : this(CreateLocalhostClient(), logger)
    {
    }

    internal AntigravityProvider(HttpClient httpClient, ILogger<AntigravityProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private static HttpClient CreateLocalhostClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
            {
                // Only allow localhost self-signed
                return message.RequestUri?.Host == "127.0.0.1";
            }
        };

        return new HttpClient(handler);
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
                if (_cachedUsage != null)
                {
                    var timeSinceRefresh = DateTime.Now - _cacheTimestamp;
                    var minutesAgo = (int)timeSinceRefresh.TotalMinutes;
                    var description = $"Last refreshed: {minutesAgo}m ago";

                    // Check if any cached reset times have passed and update if so
                    if (_cachedUsage.Details != null && _cachedUsage.Details.Any(d => d.NextResetTime.HasValue))
                    {
                        var anyResetPassed = _cachedUsage.Details
                            .Where(d => d.NextResetTime.HasValue)
                            .Any(d => {
                                var dt = d.NextResetTime!.Value;
                                return dt <= DateTime.Now;
                            });

                        if (anyResetPassed)
                        {
                            _logger.LogDebug("Antigravity reset time passed while offline; status is unknown until reconnect");
                            description += " (Status unknown until next Antigravity refresh)";

                            return new[] { new ProviderUsage
                            {
                                ProviderId = ProviderId,
                                ProviderName = "Antigravity",
                                IsAvailable = true,
                                RequestsPercentage = 0,
                                RequestsUsed = 0,
                                RequestsAvailable = _cachedUsage.RequestsAvailable,
                                Details = null,
                                AccountName = _cachedUsage.AccountName,
                                Description = description,
                                IsQuotaBased = true,
                                PlanType = PlanType.Coding
                            }};
                        }

                    }

                    if (!description.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                    {
                        description += " (Usage unknown until next Antigravity refresh)";
                    }

                    return new[] { new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "Antigravity",
                        IsAvailable = true,
                        RequestsPercentage = 0,
                        RequestsUsed = 0,
                        RequestsAvailable = _cachedUsage.RequestsAvailable,
                        Details = null,
                        AccountName = _cachedUsage.AccountName,
                        Description = description,
                        IsQuotaBased = true,
                        PlanType = PlanType.Coding
                    }};
                }
                else
                {
                    var appRunning = IsAntigravityDesktopRunning();
                    return new[] { new ProviderUsage
                    {
                        ProviderId = ProviderId,
                        ProviderName = "Antigravity",
                        IsAvailable = true,
                        RequestsPercentage = 0,
                        RequestsUsed = 0,
                        RequestsAvailable = 0,
                        Description = appRunning
                            ? "Antigravity running, waiting for language server"
                            : "Application not running",
                        IsQuotaBased = true,
                        PlanType = PlanType.Coding
                    }};
                }
            }

            foreach (var info in processInfos)
            {
                try
                {
                    var (pid, csrfToken, commandLinePort) = info;
                    var tokenPreview = csrfToken.Length > 8 ? csrfToken[..8] : csrfToken;
                    _logger.LogDebug("Checking Antigravity process: PID={Pid}, CSRF={Csrf}, PortHint={PortHint}",
                        pid, tokenPreview, commandLinePort?.ToString() ?? "none");

                    // 2. Resolve candidate ports (extension port hint + all listening loopback ports)
                    var candidatePorts = new List<int>();
                    if (commandLinePort.HasValue && commandLinePort.Value > 0)
                    {
                        candidatePorts.Add(commandLinePort.Value);
                    }

                    foreach (var listeningPort in FindListeningPorts(pid))
                    {
                        if (!candidatePorts.Contains(listeningPort))
                        {
                            candidatePorts.Add(listeningPort);
                        }
                    }

                    if (!candidatePorts.Any())
                    {
                        throw new Exception($"No candidate Antigravity ports discovered for PID {pid}");
                    }

                    // 3. Request
                    List<ProviderUsage>? usageItems = null;
                    Exception? lastPortException = null;
                    foreach (var candidatePort in candidatePorts)
                    {
                        try
                        {
                            usageItems = await FetchUsage(candidatePort, csrfToken, config);
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastPortException = ex;
                            _logger.LogDebug(ex, "Antigravity PID={Pid} probe failed on port {Port}", pid, candidatePort);
                        }
                    }

                    if (usageItems == null)
                    {
                        throw new Exception(
                            $"Failed to fetch Antigravity usage for PID {pid} on ports [{string.Join(", ", candidatePorts)}]",
                            lastPortException);
                    }

                    // Check for duplicates based on AccountName (Email) for the MAIN item
                    // Assuming the first item is the summary
                    var mainItem = usageItems.FirstOrDefault(u => u.ProviderId == ProviderId);
                    
                    if (mainItem != null && results.Any(r => r.ProviderId == ProviderId && r.AccountName == mainItem.AccountName))
                    {
                        continue;
                    }

                    results.AddRange(usageItems);
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
                     IsAvailable = true,
                     RequestsPercentage = 0,
                     RequestsUsed = 0,
                     RequestsAvailable = 0,
                     Description = "Not running",
                     IsQuotaBased = true,
                     PlanType = PlanType.Coding
                 }};
            }

            // Cache the results for next refresh
            if (results.Any())
            {
                _cachedUsage = results.FirstOrDefault();
                _cacheTimestamp = DateTime.Now;
            }

            // Start with just the summary
            // But we can't just return results list here because we build it differently now
            // The loop above adds to 'results'
            
            // Wait, previous logic was:
            // 1. Find processes
            // 2. Add to results
            // 3. Return results
            
            // New logic inside FetchUsage returns a list (or we need to change FetchUsage signature)
            // Let's change FetchUsage to return IEnumerable<ProviderUsage>
            
            // For now, let's keep FetchUsage returning single and split it here?
            // No, FetchUsage has the context (details list). 
            
            // Refactoring FetchUsage to return List<ProviderUsage>
            
            // ... (See below for FetchUsage refactor)
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Antigravity check failed");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Antigravity",
                IsAvailable = true,
                RequestsPercentage = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                Description = IsAntigravityDesktopRunning()
                    ? "Antigravity running, waiting for language server"
                    : "Application not running",
                IsQuotaBased = true,
                PlanType = PlanType.Coding
            }};
        }
    }

    private List<(int Pid, string Token, int? Port)> FindProcessInfos()
    {
        if (DateTime.Now - _lastProcessCheck < TimeSpan.FromSeconds(30) && _cachedProcessInfos != null)
        {
            return _cachedProcessInfos;
        }

        var candidates = new List<(int Pid, string Token, int? Port)>();

        if (OperatingSystem.IsWindows())
        {
             try 
             {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE 'language_server%'");
                using var collection = searcher.Get();

                foreach (var obj in collection)
                {
                    var cmdLine = obj["CommandLine"]?.ToString();
                    var pidVal = obj["ProcessId"];

                    if (string.IsNullOrWhiteSpace(cmdLine) || pidVal == null)
                    {
                        continue;
                    }

                    if (!cmdLine.Contains("antigravity", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var token = ParseCsrfToken(cmdLine);
                    if (string.IsNullOrEmpty(token))
                    {
                        continue;
                    }

                    var pid = Convert.ToInt32(pidVal);
                    var port = ParseExtensionServerPort(cmdLine);
                    candidates.Add((pid, token, port));
                }
             }
             catch (Exception ex)
             {
                 _logger.LogError(ex, "Process discovery failed");
             }
        }
        
        candidates = candidates
            .GroupBy(c => c.Pid)
            .Select(g => g.First())
            .ToList();

        _cachedProcessInfos = candidates;
        _lastProcessCheck = DateTime.Now;
        return candidates;
    }

    private static string? ParseCsrfToken(string commandLine)
    {
        var match = Regex.Match(commandLine, @"--csrf[_-]token(?:=|\s+)([a-zA-Z0-9-]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static int? ParseExtensionServerPort(string commandLine)
    {
        var match = Regex.Match(commandLine, @"--extension_server_port(?:=|\s+)(\d+)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var port))
        {
            return port;
        }

        return null;
    }

    private static bool IsAntigravityDesktopRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return Process.GetProcessesByName("Antigravity").Any();
        }
        catch
        {
            return false;
        }
    }

    private List<int> FindListeningPorts(int pid)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "netstat",
            Arguments = "-ano",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return new List<int>();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var regex = new Regex($@"\s+TCP\s+(?:127\.0\.0\.1|\[::1\]):(\d+)\s+.*LISTENING\s+{pid}");
        var ports = new List<int>();

        foreach (var line in lines)
        {
            var match = regex.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsedPort))
            {
                if (!ports.Contains(parsedPort))
                {
                    ports.Add(parsedPort);
                }
            }
        }

        return ports;
    }

    private async Task<List<ProviderUsage>> FetchUsage(int port, string csrfToken, ProviderConfig config)
    {
        var body = new { metadata = new { ideName = "antigravity", extensionName = "antigravity", ideVersion = "unknown", locale = "en" } };
        string? responseString = null;
        Exception? lastRequestException = null;

        foreach (var scheme in new[] { "https", "http" })
        {
            var url = $"{scheme}://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUserStatus";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("X-Codeium-Csrf-Token", csrfToken);
            request.Headers.Add("Connect-Protocol-Version", "1");
            request.Content = JsonContent.Create(body);

            try
            {
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                responseString = await response.Content.ReadAsStringAsync();
                _logger.LogDebug(
                    "[Antigravity] Raw response from {Scheme} port {Port}: {Response}",
                    scheme.ToUpperInvariant(),
                    port,
                    responseString[..Math.Min(500, responseString.Length)]);
                break;
            }
            catch (Exception ex)
            {
                lastRequestException = ex;
                _logger.LogDebug(ex, "[Antigravity] Request failed for {Scheme}://127.0.0.1:{Port}", scheme, port);
            }
        }

        if (responseString == null)
        {
            throw new HttpRequestException($"No successful Antigravity response on port {port}", lastRequestException);
        }
        
        var data = JsonSerializer.Deserialize<AntigravityResponse>(responseString);

        if (data?.UserStatus == null) throw new Exception("Invalid Antigravity response");

        _logger.LogDebug("[Antigravity] Email: {Email}, Models: {ModelCount}", 
            PrivacyHelper.MaskContent(data.UserStatus.Email ?? ""), 
            data.UserStatus.CascadeModelConfigData?.ClientModelConfigs?.Count ?? 0);

        var modelConfigs = data.UserStatus.CascadeModelConfigData?.ClientModelConfigs ?? new List<ClientModelConfig>();
        var modelSorts = data.UserStatus.CascadeModelConfigData?.ClientModelSorts ?? new List<ClientModelSort>();
        var (labelToGroup, masterModelLabels) = BuildGroupingMetadata(modelSorts, modelConfigs);
        var configMap = BuildConfigMap(modelConfigs);

        var details = new List<ProviderUsageDetail>();
        double? minRemaining = null;
        foreach (var label in masterModelLabels)
        {
            configMap.TryGetValue(label, out var modelConfig);
            var remainingPct = ResolveRemainingPercentage(label, modelConfig);
            if (!remainingPct.HasValue)
            {
                continue;
            }

            var (resetDescription, nextResetTime) = ResolveResetInfo(modelConfig);
            var modelName = ResolveDisplayModelName(label);

            details.Add(new ProviderUsageDetail
            {
                Name = label,
                ModelName = modelName,
                GroupName = labelToGroup.TryGetValue(label, out var groupName)
                    ? groupName
                    : "Ungrouped Models",
                Used = $"{remainingPct.Value.ToString("F0", CultureInfo.InvariantCulture)}%",
                Description = resetDescription,
                NextResetTime = nextResetTime
            });

            minRemaining = minRemaining.HasValue
                ? Math.Min(minRemaining.Value, remainingPct.Value)
                : remainingPct.Value;
        }

        if (!minRemaining.HasValue)
        {
            _cachedUsage = null;
            _cacheTimestamp = DateTime.Now;
            return new List<ProviderUsage>
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Antigravity",
                    IsAvailable = true,
                    RequestsPercentage = 0,
                    RequestsUsed = 0,
                    RequestsAvailable = 0,
                    UsageUnit = "Quota %",
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    Description = "Usage unknown (no model quota data)",
                    AccountName = data.UserStatus.Email ?? string.Empty
                }
            };
        }

        var sortedDetails = SortDetails(details);
        var summary = BuildSummaryUsage(data.UserStatus, sortedDetails, minRemaining.Value);
        ApplySummaryRawNumbers(summary, configMap);

        var results = BuildChildUsages(sortedDetails, configMap, config, data.UserStatus.Email ?? string.Empty);
        results.Insert(0, summary);

        _cachedUsage = summary;
        _cacheTimestamp = DateTime.Now;

        return results;
    }

    private static (Dictionary<string, string> LabelToGroup, List<string> MasterModelLabels) BuildGroupingMetadata(
        List<ClientModelSort> modelSorts,
        List<ClientModelConfig> modelConfigs)
    {
        var labelToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var masterModelLabelSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var masterModelLabels = new List<string>();

        for (var sortIndex = 0; sortIndex < modelSorts.Count; sortIndex++)
        {
            var modelSort = modelSorts[sortIndex];
            var sortName = ResolveModelSortName(modelSort, sortIndex);
            var modelGroups = modelSort.Groups ?? new List<ModelGroup>();

            for (var groupIndex = 0; groupIndex < modelGroups.Count; groupIndex++)
            {
                var groupName = ResolveModelGroupName(modelGroups[groupIndex], sortName, groupIndex);
                foreach (var label in GetModelLabels(modelGroups[groupIndex]))
                {
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    if (masterModelLabelSet.Add(label))
                    {
                        masterModelLabels.Add(label);
                    }

                    if (!labelToGroup.ContainsKey(label))
                    {
                        labelToGroup[label] = groupName;
                    }
                }
            }
        }

        if (!masterModelLabels.Any())
        {
            masterModelLabels = modelConfigs
                .Where(c => !string.IsNullOrWhiteSpace(c.Label))
                .Select(c => c.Label!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return (labelToGroup, masterModelLabels);
    }

    private static Dictionary<string, ClientModelConfig> BuildConfigMap(List<ClientModelConfig> modelConfigs)
    {
        return modelConfigs
            .Where(c => !string.IsNullOrEmpty(c.Label))
            .ToDictionary(c => c.Label!, c => c);
    }

    private double? ResolveRemainingPercentage(string label, ClientModelConfig? modelConfig)
    {
        if (modelConfig == null)
        {
            _logger.LogDebug("[Antigravity] Model {Label} not found in config map, usage unknown", label);
            return null;
        }

        _logger.LogDebug("[Antigravity] Model {Label}: RemainingFraction={Rem}, TotalRequests={Total}, UsedRequests={Used}",
            label,
            modelConfig.QuotaInfo?.RemainingFraction?.ToString() ?? "null",
            modelConfig.QuotaInfo?.TotalRequests?.ToString() ?? "null",
            modelConfig.QuotaInfo?.UsedRequests?.ToString() ?? "null");

        if (modelConfig.QuotaInfo?.RemainingFraction.HasValue == true)
        {
            return Math.Max(0, Math.Min(100, modelConfig.QuotaInfo.RemainingFraction.Value * 100));
        }

        if (modelConfig.QuotaInfo?.TotalRequests.HasValue == true && modelConfig.QuotaInfo.TotalRequests > 0)
        {
            var used = modelConfig.QuotaInfo.UsedRequests ?? 0;
            var remaining = Math.Max(0, modelConfig.QuotaInfo.TotalRequests.Value - used);
            var remainingPct = (remaining / (double)modelConfig.QuotaInfo.TotalRequests.Value) * 100;
            return Math.Max(0, Math.Min(100, remainingPct));
        }

        _logger.LogDebug("[Antigravity] Model {Label} missing quota fields, usage unknown", label);
        return null;
    }

    private static (string Description, DateTime? NextResetTime) ResolveResetInfo(ClientModelConfig? modelConfig)
    {
        if (string.IsNullOrEmpty(modelConfig?.QuotaInfo?.ResetTime))
        {
            return (string.Empty, null);
        }

        if (!DateTime.TryParse(modelConfig.QuotaInfo.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return (string.Empty, null);
        }

        var localReset = dt.ToLocalTime();
        var diff = localReset - DateTime.Now;
        if (diff.TotalSeconds <= 0)
        {
            return (string.Empty, null);
        }

        return ($" (Resets: ({localReset:MMM dd HH:mm}))", localReset);
    }

    private static string ResolveDisplayModelName(string label)
    {
        return label;
    }

    private static List<ProviderUsageDetail> SortDetails(List<ProviderUsageDetail> details)
    {
        return details
            .OrderBy(d => d.Name.StartsWith("[Credits]") ? "0" + d.Name : "1" + d.Name)
            .ToList();
    }

    private ProviderUsage BuildSummaryUsage(UserStatus userStatus, List<ProviderUsageDetail> sortedDetails, double remainingPctTotal)
    {
        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Antigravity",
            RequestsPercentage = remainingPctTotal,
            RequestsUsed = 100 - remainingPctTotal,
            RequestsAvailable = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            Description = $"{remainingPctTotal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining",
            Details = sortedDetails,
            AccountName = userStatus.Email ?? string.Empty,
            NextResetTime = sortedDetails.Where(d => d.NextResetTime.HasValue).OrderBy(d => d.NextResetTime).FirstOrDefault()?.NextResetTime
        };
    }

    private static void ApplySummaryRawNumbers(ProviderUsage summary, Dictionary<string, ClientModelConfig> configMap)
    {
        long totalLimit = 0;
        long totalUsed = 0;
        var hasRawNumbers = false;

        foreach (var cfg in configMap.Values)
        {
            if (cfg.QuotaInfo?.TotalRequests.HasValue == true)
            {
                totalLimit += cfg.QuotaInfo.TotalRequests.Value;
                totalUsed += cfg.QuotaInfo.UsedRequests ?? 0;
                hasRawNumbers = true;
            }
        }

        if (!hasRawNumbers || totalLimit <= 0)
        {
            return;
        }

        summary.RequestsAvailable = totalLimit;
        summary.RequestsUsed = totalUsed;
        summary.UsageUnit = "Tokens";
        summary.DisplayAsFraction = true;
    }

    private List<ProviderUsage> BuildChildUsages(
        List<ProviderUsageDetail> sortedDetails,
        Dictionary<string, ClientModelConfig> configMap,
        ProviderConfig config,
        string accountName)
    {
        var results = new List<ProviderUsage>();

        foreach (var detail in sortedDetails)
        {
            var detailRemaining = ParseDetailRemainingPercentage(detail.Used);
            var (childId, childName) = ResolveChildIdentity(detail, config);

            long? detailTotal = null;
            long? detailUsed = null;
            if (configMap.TryGetValue(detail.Name, out var cfg) && cfg.QuotaInfo != null)
            {
                detailTotal = cfg.QuotaInfo.TotalRequests;
                detailUsed = cfg.QuotaInfo.UsedRequests;
            }

            var childUsage = new ProviderUsage
            {
                ProviderId = childId,
                ProviderName = childName,
                RequestsPercentage = detailRemaining,
                RequestsUsed = 100 - detailRemaining,
                RequestsAvailable = 100,
                UsageUnit = "Quota %",
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = $"{detailRemaining.ToString("F0", CultureInfo.InvariantCulture)}% Remaining",
                AccountName = accountName,
                IsAvailable = true,
                FetchedAt = DateTime.UtcNow,
                AuthSource = "antigravity",
                DisplayAsFraction = detailTotal.HasValue && detailTotal > 0,
                NextResetTime = detail.NextResetTime
            };

            if (detailTotal.HasValue && detailTotal > 0)
            {
                childUsage.RequestsAvailable = detailTotal.Value;
                childUsage.RequestsUsed = detailUsed ?? 0;
                childUsage.UsageUnit = "Tokens";
            }

            results.Add(childUsage);
        }

        return results;
    }

    private static double ParseDetailRemainingPercentage(string detailUsed)
    {
        if (detailUsed.EndsWith("%") &&
            double.TryParse(detailUsed.TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private (string ChildId, string ChildName) ResolveChildIdentity(ProviderUsageDetail detail, ProviderConfig config)
    {
        var childId = $"{ProviderId}.{detail.Name.ToLowerInvariant().Replace(" ", "-")}";
        var childName = "Antigravity " + detail.Name;

        if (config.Models == null || !config.Models.Any())
        {
            return (childId, childName);
        }

        var match = config.Models.FirstOrDefault(m =>
            m.Id.Equals(detail.Name, StringComparison.OrdinalIgnoreCase) ||
            m.Name.Equals(detail.Name, StringComparison.OrdinalIgnoreCase) ||
            m.Matches.Any(x => x.Equals(detail.Name, StringComparison.OrdinalIgnoreCase)));

        if (match == null)
        {
            return (childId, childName);
        }

        if (!string.IsNullOrWhiteSpace(match.Id))
        {
            childId = $"{ProviderId}.{match.Id}";
        }

        if (!string.IsNullOrWhiteSpace(match.Name))
        {
            childName = match.Name;
        }

        return (childId, childName);
    }

    private static List<string> GetModelLabels(ModelGroup group)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (group.ModelLabels != null)
        {
            foreach (var label in group.ModelLabels)
            {
                if (!string.IsNullOrWhiteSpace(label))
                {
                    labels.Add(label);
                }
            }
        }

        if (group.ExtensionData != null)
        {
            foreach (var key in new[] { "labels", "modelLabels", "models", "items", "model_ids", "modelIds" })
            {
                if (!group.ExtensionData.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            labels.Add(value);
                        }
                    }
                    else if (item.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var property in new[] { "label", "name", "modelLabel", "model_name" })
                        {
                            if (!item.TryGetProperty(property, out var valueElement) || valueElement.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var value = valueElement.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                labels.Add(value);
                            }
                        }
                    }
                }
            }
        }

        return labels.ToList();
    }

    private static string ResolveModelSortName(ClientModelSort sort, int index)
    {
        if (!string.IsNullOrWhiteSpace(sort.Name))
        {
            return sort.Name;
        }

        if (!string.IsNullOrWhiteSpace(sort.Label))
        {
            return sort.Label;
        }

        if (!string.IsNullOrWhiteSpace(sort.Title))
        {
            return sort.Title;
        }

        if (!string.IsNullOrWhiteSpace(sort.SortId))
        {
            return sort.SortId;
        }

        if (sort.ExtensionData != null)
        {
            foreach (var key in new[] { "name", "label", "title", "id", "sortName", "sort_name" })
            {
                if (sort.ExtensionData.TryGetValue(key, out var element))
                {
                    var value = TryReadStringFromJsonElement(element);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }

        return $"Sort {index + 1}";
    }

    private static string ResolveModelGroupName(ModelGroup group, string sortName, int index)
    {
        if (!string.IsNullOrWhiteSpace(group.Name))
        {
            return group.Name;
        }

        if (!string.IsNullOrWhiteSpace(group.Label))
        {
            return group.Label;
        }

        if (!string.IsNullOrWhiteSpace(group.Title))
        {
            return group.Title;
        }

        if (!string.IsNullOrWhiteSpace(group.GroupName))
        {
            return group.GroupName;
        }

        if (!string.IsNullOrWhiteSpace(group.DisplayName))
        {
            return group.DisplayName;
        }

        if (group.ExtensionData != null)
        {
            foreach (var key in new[] { "name", "label", "title", "groupName", "displayName", "group_label", "group_name" })
            {
                if (group.ExtensionData.TryGetValue(key, out var element))
                {
                    var value = TryReadStringFromJsonElement(element);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }
        }

        return string.IsNullOrWhiteSpace(sortName)
            ? $"Group {index + 1}"
            : $"{sortName} Group {index + 1}";
    }

    private static string? TryReadStringFromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "name", "label", "title", "displayName" })
            {
                if (element.TryGetProperty(key, out var nested) && nested.ValueKind == JsonValueKind.String)
                {
                    return nested.GetString();
                }
            }
        }

        return null;
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
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("id")]
        public string? SortId { get; set; }

        [JsonPropertyName("groups")]
        public List<ModelGroup>? Groups { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private class ModelGroup
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("groupName")]
        public string? GroupName { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("modelLabels")]
        public List<string>? ModelLabels { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private class ClientModelConfig
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("modelOrAlias")]
        public ModelOrAlias? ModelOrAlias { get; set; }
        
        [JsonPropertyName("quotaInfo")]
        public QuotaInfo? QuotaInfo { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? ExtensionData { get; set; }
    }

    private class ModelOrAlias
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("alias")]
        public string? Alias { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

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

