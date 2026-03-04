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

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // Check if CLI exists first
        var pathExists = _cliPath == "opencode"
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
                    AuthSource = config.AuthSource
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
                    AuthSource = config.AuthSource
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
        var totalCost = 0.0;
        var sessions = 0;
        var messages = 0;
        var avgCostPerDay = 0.0;

        // Clean ANSI codes (simplified - remove common escape sequences)
        var cleaned = output
            .Replace("\u001b[", "")
            .Replace("0m", "")
            .Replace("1m", "")
            .Replace("32m", "")
            .Replace("36m", "")
            .Replace("90m", "");

        // Parse Total Cost
        var costMatch = Regex.Match(cleaned, @"Total Cost\s+\$([0-9.]+)");
        if (costMatch.Success && costMatch.Groups.Count > 1)
        {
            double.TryParse(costMatch.Groups[1].Value, out totalCost);
        }

        // Parse Sessions
        var sessionsMatch = Regex.Match(cleaned, @"Sessions\s+([0-9,]+)");
        if (sessionsMatch.Success && sessionsMatch.Groups.Count > 1)
        {
            int.TryParse(sessionsMatch.Groups[1].Value.Replace(",", ""), out sessions);
        }

        // Parse Messages
        var messagesMatch = Regex.Match(cleaned, @"Messages\s+([0-9,]+)");
        if (messagesMatch.Success && messagesMatch.Groups.Count > 1)
        {
            int.TryParse(messagesMatch.Groups[1].Value.Replace(",", ""), out messages);
        }

        // Parse Avg Cost/Day
        var avgCostMatch = Regex.Match(cleaned, @"Avg Cost/Day\s+\$([0-9.]+)");
        if (avgCostMatch.Success && avgCostMatch.Groups.Count > 1)
        {
            double.TryParse(avgCostMatch.Groups[1].Value, out avgCostPerDay);
        }

        var details = new List<ProviderUsageDetail>
        {
            new ProviderUsageDetail
            {
                Name = "Sessions",
                Description = $"{sessions} sessions",
                DetailType = ProviderUsageDetailType.Other,
                WindowKind = WindowKind.None
            },
            new ProviderUsageDetail
            {
                Name = "Messages",
                Description = $"{messages} messages",
                DetailType = ProviderUsageDetailType.Other,
                WindowKind = WindowKind.None
            },
            new ProviderUsageDetail
            {
                Name = "Avg Cost/Day",
                Description = $"${avgCostPerDay:F2}",
                DetailType = ProviderUsageDetailType.Other,
                WindowKind = WindowKind.None
            }
        };

        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "OpenCode Zen",
            RequestsPercentage = 0.0, // Pay as you go, no limit
            RequestsUsed = totalCost,
            RequestsAvailable = 0.0,
            UsageUnit = "USD",
            IsQuotaBased = false,
            PlanType = PlanType.Usage,
            IsAvailable = true,
            Description = $"${totalCost:F2} ({sessions} sessions, {messages} msgs)",
            Details = details,
            AuthSource = config.AuthSource
        };
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


