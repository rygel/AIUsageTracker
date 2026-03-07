using System.Diagnostics;
using System.Text.RegularExpressions;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenCodeZenProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "opencode-zen",
        displayName: "OpenCode Zen",
        planType: PlanType.Usage,
        isQuotaBased: false,
        defaultConfigType: "pay-as-you-go",
        autoIncludeWhenUnconfigured: true);

    public override ProviderDefinition Definition => StaticDefinition;
    public override string ProviderId => StaticDefinition.ProviderId;
    private readonly ILogger<OpenCodeZenProvider> _logger;
    private string _cliPath;

    public OpenCodeZenProvider(ILogger<OpenCodeZenProvider> logger)
    {
        _logger = logger;
        // Default path - should be configurable in real app
        _cliPath = OperatingSystem.IsWindows()
            ? @"C:\Users\Alexander\AppData\Roaming\npm\opencode.cmd"
            : "opencode";
    }

    public OpenCodeZenProvider(ILogger<OpenCodeZenProvider> logger, string cliPath) : this(logger)
    {
        _cliPath = cliPath;
    }

    private static readonly Regex[] CleanupPatterns = new[]
    {
        new Regex("\u001b\\[[0-9;]*m", RegexOptions.Compiled),
        new Regex("\u001b\\[K", RegexOptions.Compiled),
        new Regex("[0-9]+A", RegexOptions.Compiled),
        new Regex("\u001b\\[[0-9]*;[0-9]*m", RegexOptions.Compiled)
    };

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // Check if CLI exists first
        var pathExists = string.Equals(_cliPath, "opencode", StringComparison.OrdinalIgnoreCase)
            ? await IsInPath("opencode")
            : File.Exists(_cliPath);

        if (!pathExists)
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenCode Zen",
                    IsAvailable = false,
                    Description = "CLI not found at expected path",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    AuthSource = config.AuthSource,
                    RawJson = $"CLI not found at path: {_cliPath}",
                    HttpStatus = 404
                }
            };
        }

        try
        {
            var output = await RunCliAsync();
            return new[] { ParseOutput(output, config) };
        }
        catch (Exception ex)
        {
            _logger.LogWarning("OpenCode CLI failed: {Message}", ex.Message);
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "OpenCode Zen",
                    IsAvailable = false,
                    Description = $"CLI Error: {ex.Message} (Check log or clear storage if JSON error)",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    AuthSource = config.AuthSource,
                    RawJson = ex.ToString(),
                    HttpStatus = 500
                }
            };
        }
    }

    private async Task<string> RunCliAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = _cliPath,
            Arguments = "stats --days 7 --models 10",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new Exception("Failed to start OpenCode CLI");
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(cts.Token);

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new Exception($"CLI Error: {process.ExitCode} - {stderr}");
        }

        return await process.StandardOutput.ReadToEndAsync();
    }

    private ProviderUsage ParseOutput(string output, ProviderConfig config)
    {
        var cleaned = CleanAnsiOutput(output);
        
        var totalCost = ParseValue<double>(cleaned, @"Total Cost\s+\$([0-9.]+)");
        var sessions = ParseValue<int>(cleaned, @"Sessions\s+([0-9,]+)");
        var messages = ParseValue<int>(cleaned, @"Messages\s+([0-9,]+)");
        var days = ParseValue<int>(cleaned, @"Days\s+(\d+)");
        var avgCostPerDay = ParseValue<double>(cleaned, @"Avg Cost/Day\s+\$([0-9.]+)");
        var avgTokensPerSession = ParseValue<double>(cleaned, @"Avg Tokens/Session\s+([0-9.,KM]+)");
        var medianTokensPerSession = ParseValue<double>(cleaned, @"Median Tokens/Session\s+([0-9.,KM]+)");
        var inputTokens = ParseValue<double>(cleaned, @"Input\s+([0-9.,KM]+)");
        var outputTokens = ParseValue<double>(cleaned, @"Output\s+([0-9.,KM]+)");
        var cacheRead = ParseValue<double>(cleaned, @"Cache Read\s+([0-9.,KM]+)");
        var cacheWrite = ParseValue<double>(cleaned, @"Cache Write\s+([0-9.,KM]+)");

        var details = new List<ProviderUsageDetail>();
        
        // Add overview details
        details.Add(new ProviderUsageDetail
        {
            Name = "Sessions",
            Description = $"{sessions:N0} sessions",
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None
        });
        
        details.Add(new ProviderUsageDetail
        {
            Name = "Messages",
            Description = $"{messages:N0} messages",
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None
        });
        
        details.Add(new ProviderUsageDetail
        {
            Name = "Avg Cost/Day",
            Description = $"${avgCostPerDay:F2}",
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None
        });

        // Add token statistics
        details.Add(new ProviderUsageDetail
        {
            Name = "Input Tokens",
            Description = FormatTokens(inputTokens),
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None
        });
        
        details.Add(new ProviderUsageDetail
        {
            Name = "Output Tokens",
            Description = FormatTokens(outputTokens),
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None
        });
        
        details.Add(new ProviderUsageDetail
        {
            Name = "Cache Read",
            Description = FormatTokens(cacheRead),
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None
        });
        
        details.Add(new ProviderUsageDetail
        {
            Name = "Cache Write",
            Description = FormatTokens(cacheWrite),
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None
        });

        // Parse and add per-model breakdown
        var modelUsage = ParseModelUsage(cleaned);
        foreach (var model in modelUsage.Take(5)) // Limit to top 5 models
        {
            details.Add(new ProviderUsageDetail
            {
                Name = model.Name,
                Description = $"{model.Messages:N0} msgs | {FormatTokens(model.Tokens)} | ${model.Cost:F2}",
                DetailType = ProviderUsageDetailType.Other,
                WindowKind = WindowKind.None
            });
        }

        // Parse and add tool usage
        var toolUsage = ParseToolUsage(cleaned);
        foreach (var tool in toolUsage.Take(5)) // Limit to top 5 tools
        {
            details.Add(new ProviderUsageDetail
            {
                Name = $"Tool: {tool.Name}",
                Description = $"{tool.Count:N0} uses ({tool.Percentage:F1}%)",
                DetailType = ProviderUsageDetailType.Other,
                WindowKind = WindowKind.None
            });
        }

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "OpenCode Zen",
            RequestsPercentage = 0.0,
            RequestsUsed = totalCost,
            RequestsAvailable = 0.0,
            UsageUnit = "USD",
            IsQuotaBased = false,
            PlanType = PlanType.Usage,
            IsAvailable = true,
            Description = $"${totalCost:F2} ({sessions} sessions, {messages} msgs, {days} days)",
            Details = details,
            AuthSource = config.AuthSource,
            RawJson = output,
            HttpStatus = 200
        };
    }

    private string CleanAnsiOutput(string output)
    {
        var cleaned = output;
        foreach (var pattern in CleanupPatterns)
        {
            cleaned = pattern.Replace(cleaned, "");
        }
        return cleaned;
    }

    private T ParseValue<T>(string input, string pattern) where T : struct
    {
        var match = Regex.Match(input, pattern, RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        if (match.Success && match.Groups.Count > 1)
        {
            var valueStr = match.Groups[1].Value.Replace(",", "");
            
            if (typeof(T) == typeof(double))
            {
                if (double.TryParse(valueStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    return (T)(object)d;
                }
            }
            else if (typeof(T) == typeof(int))
            {
                if (int.TryParse(valueStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var i))
                {
                    return (T)(object)i;
                }
            }
        }
        return default;
    }

    private string FormatTokens(double tokens)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        if (tokens >= 1_000_000_000) return (tokens / 1_000_000_000).ToString("F1", culture) + "B";
        if (tokens >= 1_000_000) return (tokens / 1_000_000).ToString("F1", culture) + "M";
        if (tokens >= 1_000) return (tokens / 1_000).ToString("F1", culture) + "K";
        return tokens.ToString("F0", culture);
    }

    private List<ModelUsage> ParseModelUsage(string input)
    {
        var models = new List<ModelUsage>();
        var modelBlocks = Regex.Split(input, @"─{44,}")
            .SkipWhile(block => !block.Contains("MODEL USAGE"))
            .Skip(1)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(modelBlocks)) return models;

        var modelPattern = new Regex(@"(?<model>[^\n]+)\s+Messages\s+(?<messages>[0-9,]+)\s+Input Tokens\s+(?<input>[0-9.,KM]+)\s+Output Tokens\s+(?<output>[0-9.,KM]+)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        
        foreach (Match match in modelPattern.Matches(modelBlocks))
        {
            var model = new ModelUsage
            {
                Name = match.Groups["model"].Value.Trim(),
                Messages = int.Parse(match.Groups["messages"].Value.Replace(",", "")),
                Tokens = ParseTokenCount(match.Groups["input"].Value) + ParseTokenCount(match.Groups["output"].Value),
                Cost = 0.0
            };
            models.Add(model);
        }

        return models.OrderByDescending(m => m.Cost).ToList();
    }

    private List<ToolUsage> ParseToolUsage(string input)
    {
        var tools = new List<ToolUsage>();
        var toolBlocks = Regex.Split(input, @"─{44,}")
            .SkipWhile(block => !block.Contains("TOOL USAGE"))
            .Skip(1)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(toolBlocks)) return tools;

        var toolPattern = new Regex(@"(?<tool>\w+)\s+[█]+(?<count>[0-9]+)\s+\((?<percentage>[\d.]+)%\)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        
        foreach (Match match in toolPattern.Matches(toolBlocks))
        {
            var tool = new ToolUsage
            {
                Name = match.Groups["tool"].Value,
                Count = int.Parse(match.Groups["count"].Value),
                Percentage = double.Parse(match.Groups["percentage"].Value)
            };
            tools.Add(tool);
        }

        return tools.OrderByDescending(t => t.Count).ToList();
    }

    private double ParseTokenCount(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        
        var cleaned = value.Replace(",", "");
        if (cleaned.EndsWith("B")) return double.Parse(cleaned[..^1]) * 1_000_000_000;
        if (cleaned.EndsWith("M")) return double.Parse(cleaned[..^1]) * 1_000_000;
        if (cleaned.EndsWith("K")) return double.Parse(cleaned[..^1]) * 1_000;
        
        return double.Parse(cleaned);
    }

    private class ModelUsage
    {
        public string Name { get; set; } = string.Empty;
        public int Messages { get; set; }
        public double Tokens { get; set; }
        public double Cost { get; set; }
    }

    private class ToolUsage
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    private async Task<bool> IsInPath(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await process.WaitForExitAsync(cts.Token);
                    return process.ExitCode == 0;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("IsInPath check failed: {Message}", ex.Message);
        }

        return false;
    }
}


