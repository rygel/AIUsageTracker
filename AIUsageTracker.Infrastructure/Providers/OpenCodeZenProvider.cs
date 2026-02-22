using System.Diagnostics;
using System.Text.RegularExpressions;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenCodeZenProvider : IProviderService
{
    public string ProviderId => "opencode-zen";
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

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // Check if CLI exists first
        var pathExists = _cliPath == "opencode"
            ? IsInPath("opencode")
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
            Description = $"${totalCost:F2} (7 days)",
            AuthSource = config.AuthSource
        };
    }

    private bool IsInPath(string command)
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
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }
        catch { }

        return false;
    }
}

