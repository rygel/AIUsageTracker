// <copyright file="OpenCodeZenProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenCodeZenProvider : ProviderBase
{
    private const string ProviderDisplayName = "OpenCode Zen";
    private const string DefaultCliCommand = "opencode";
    private static readonly Regex[] CleanupPatterns =
    {
        new("\u001b\\[[0-9;]*m", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
        new("\u001b\\[K", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
        new("[0-9]+A", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
        new("\u001b\\[[0-9]*;[0-9]*m", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
    };

    private static readonly Regex SeparatorRegex = new(
        @"─{44,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ModelUsageRegex = new(
        @"(?<model>[^\n]+)\s+Messages\s+(?<messages>[0-9,]+)\s+Input Tokens\s+(?<input>[0-9.,KM]+)\s+Output Tokens\s+(?<output>[0-9.,KM]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ToolUsageRegex = new(
        @"(?<tool>\w+)\s+[█]+(?<count>[0-9]+)\s+\((?<percentage>[\d.]+)%\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1));

    private readonly ILogger<OpenCodeZenProvider> _logger;
    private string _cliPath;

    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "opencode-zen",
        displayName: ProviderDisplayName,
        planType: PlanType.Usage,
        isQuotaBased: false,
        defaultConfigType: "pay-as-you-go",
        autoIncludeWhenUnconfigured: true,
        handledProviderIds: new[] { "opencode-zen", "opencode-go" },
        displayNameOverrides: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["opencode-go"] = "Opencode Go",
        });

    public OpenCodeZenProvider(ILogger<OpenCodeZenProvider> logger)
    {
        this._logger = logger;
        this._cliPath = OperatingSystem.IsWindows()
            ? @"C:\Users\Alexander\AppData\Roaming\npm\opencode.cmd"
            : DefaultCliCommand;
    }

    public OpenCodeZenProvider(ILogger<OpenCodeZenProvider> logger, string cliPath)
        : this(logger)
    {
        this._cliPath = cliPath;
    }

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null)
    {
        var pathExists = string.Equals(this._cliPath, DefaultCliCommand, StringComparison.OrdinalIgnoreCase)
            ? await this.IsInPathAsync(DefaultCliCommand).ConfigureAwait(false)
            : File.Exists(this._cliPath);

        if (!pathExists)
        {
            return new[]
            {
                CreateUnavailableUsage(
                    this.ProviderId,
                    "CLI not found at expected path",
                    config.AuthSource,
                    $"CLI not found at path: {this._cliPath}",
                    404),
            };
        }

        try
        {
            var output = await this.RunCliAsync().ConfigureAwait(false);
            return new[] { this.ParseOutput(output, config) };
        }
        catch (Exception ex)
        {
            this._logger.LogWarning("OpenCode CLI failed: {Message}", ex.Message);
            return new[]
            {
                CreateUnavailableUsage(
                    this.ProviderId,
                    $"CLI Error: {ex.Message} (Check log or clear storage if JSON error)",
                    config.AuthSource,
                    ex.ToString(),
                    500),
            };
        }
    }

    private static ProviderUsage CreateUnavailableUsage(
        string providerId,
        string description,
        string? authSource,
        string rawJson,
        int httpStatus)
    {
        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = ProviderDisplayName,
            IsAvailable = false,
            Description = description,
            IsQuotaBased = false,
            PlanType = PlanType.Usage,
            AuthSource = authSource,
            RawJson = rawJson,
            HttpStatus = httpStatus,
        };
    }

    private static ProviderUsage CreateUsage(
        string providerId,
        ProviderConfig config,
        string rawOutput,
        double totalCost,
        int sessions,
        int messages,
        int days,
        List<ProviderUsageDetail> details)
    {
        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = ProviderDisplayName,
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
            RawJson = rawOutput,
            HttpStatus = 200,
        };
    }

    private static List<ProviderUsageDetail> BuildDetails(string cleaned)
    {
        var details = new List<ProviderUsageDetail>();
        AddOverviewDetails(details, cleaned);
        AddTokenDetails(details, cleaned);
        AddModelDetails(details, cleaned);
        AddToolDetails(details, cleaned);
        return details;
    }

    private static void AddOverviewDetails(List<ProviderUsageDetail> details, string cleaned)
    {
        var sessions = ParseValue<int>(cleaned, @"Sessions\s+([0-9,]+)");
        var messages = ParseValue<int>(cleaned, @"Messages\s+([0-9,]+)");
        var avgCostPerDay = ParseValue<double>(cleaned, @"Avg Cost/Day\s+\$([0-9.]+)");

        details.Add(CreateOtherDetail("Sessions", $"{sessions:N0} sessions"));
        details.Add(CreateOtherDetail("Messages", $"{messages:N0} messages"));
        details.Add(CreateOtherDetail("Avg Cost/Day", $"${avgCostPerDay:F2}"));
    }

    private static void AddTokenDetails(List<ProviderUsageDetail> details, string cleaned)
    {
        var inputTokens = ParseValue<double>(cleaned, @"Input\s+([0-9.,KM]+)");
        var outputTokens = ParseValue<double>(cleaned, @"Output\s+([0-9.,KM]+)");
        var cacheRead = ParseValue<double>(cleaned, @"Cache Read\s+([0-9.,KM]+)");
        var cacheWrite = ParseValue<double>(cleaned, @"Cache Write\s+([0-9.,KM]+)");

        details.Add(CreateOtherDetail("Input Tokens", FormatTokens(inputTokens)));
        details.Add(CreateOtherDetail("Output Tokens", FormatTokens(outputTokens)));
        details.Add(CreateOtherDetail("Cache Read", FormatTokens(cacheRead)));
        details.Add(CreateOtherDetail("Cache Write", FormatTokens(cacheWrite)));
    }

    private static void AddModelDetails(List<ProviderUsageDetail> details, string cleaned)
    {
        foreach (var model in ParseModelUsage(cleaned).Take(5))
        {
            details.Add(CreateOtherDetail(
                model.Name,
                $"{model.Messages:N0} msgs | {FormatTokens(model.Tokens)} | ${model.Cost:F2}"));
        }
    }

    private static void AddToolDetails(List<ProviderUsageDetail> details, string cleaned)
    {
        foreach (var tool in ParseToolUsage(cleaned).Take(5))
        {
            details.Add(CreateOtherDetail(
                $"Tool: {tool.Name}",
                $"{tool.Count:N0} uses ({tool.Percentage:F1}%)"));
        }
    }

    private static ProviderUsageDetail CreateOtherDetail(string name, string description)
    {
        return new ProviderUsageDetail
        {
            Name = name,
            Description = description,
            DetailType = ProviderUsageDetailType.Other,
            WindowKind = WindowKind.None,
        };
    }

    private static string CleanAnsiOutput(string output)
    {
        var cleaned = output;
        foreach (var pattern in CleanupPatterns)
        {
            cleaned = pattern.Replace(cleaned, string.Empty);
        }

        return cleaned;
    }

    private static T ParseValue<T>(string input, string pattern)
        where T : struct
    {
        var match = Regex.Match(
            input,
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds(1));
        if (match.Success && match.Groups.Count > 1)
        {
            var valueText = match.Groups[1].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (typeof(T) == typeof(double) &&
                double.TryParse(valueText, NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return (T)(object)doubleValue;
            }

            if (typeof(T) == typeof(int) &&
                int.TryParse(valueText, NumberStyles.Any, CultureInfo.InvariantCulture, out var intValue))
            {
                return (T)(object)intValue;
            }
        }

        return default;
    }

    private static string FormatTokens(double tokens)
    {
        if (tokens >= 1_000_000_000)
        {
            return (tokens / 1_000_000_000).ToString("F1", CultureInfo.InvariantCulture) + "B";
        }

        if (tokens >= 1_000_000)
        {
            return (tokens / 1_000_000).ToString("F1", CultureInfo.InvariantCulture) + "M";
        }

        if (tokens >= 1_000)
        {
            return (tokens / 1_000).ToString("F1", CultureInfo.InvariantCulture) + "K";
        }

        return tokens.ToString("F0", CultureInfo.InvariantCulture);
    }

    private static List<ModelUsage> ParseModelUsage(string input)
    {
        var modelBlocks = SeparatorRegex.Split(input)
            .SkipWhile(block => !block.Contains("MODEL USAGE", StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(modelBlocks))
        {
            return new List<ModelUsage>();
        }

        return ModelUsageRegex.Matches(modelBlocks)
            .Cast<Match>()
            .Select(match => new ModelUsage
            {
                Name = match.Groups["model"].Value.Trim(),
                Messages = int.Parse(
                    match.Groups["messages"].Value.Replace(",", string.Empty, StringComparison.Ordinal),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture),
                Tokens = ParseTokenCount(match.Groups["input"].Value) + ParseTokenCount(match.Groups["output"].Value),
                Cost = 0.0,
            })
            .OrderByDescending(model => model.Cost)
            .ToList();
    }

    private static List<ToolUsage> ParseToolUsage(string input)
    {
        var toolBlocks = SeparatorRegex.Split(input)
            .SkipWhile(block => !block.Contains("TOOL USAGE", StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault();
        if (string.IsNullOrEmpty(toolBlocks))
        {
            return new List<ToolUsage>();
        }

        return ToolUsageRegex.Matches(toolBlocks)
            .Cast<Match>()
            .Select(match => new ToolUsage
            {
                Name = match.Groups["tool"].Value,
                Count = int.Parse(match.Groups["count"].Value, NumberStyles.Any, CultureInfo.InvariantCulture),
                Percentage = double.Parse(match.Groups["percentage"].Value, NumberStyles.Any, CultureInfo.InvariantCulture),
            })
            .OrderByDescending(tool => tool.Count)
            .ToList();
    }

    private static double ParseTokenCount(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var cleaned = value.Replace(",", string.Empty, StringComparison.Ordinal);
        if (cleaned.EndsWith("B", StringComparison.Ordinal))
        {
            return double.Parse(cleaned[..^1], NumberStyles.Any, CultureInfo.InvariantCulture) * 1_000_000_000;
        }

        if (cleaned.EndsWith("M", StringComparison.Ordinal))
        {
            return double.Parse(cleaned[..^1], NumberStyles.Any, CultureInfo.InvariantCulture) * 1_000_000;
        }

        if (cleaned.EndsWith("K", StringComparison.Ordinal))
        {
            return double.Parse(cleaned[..^1], NumberStyles.Any, CultureInfo.InvariantCulture) * 1_000;
        }

        return double.Parse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture);
    }

    private async Task<string> RunCliAsync()
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = this._cliPath,
            Arguments = "stats --days 7 --models 10",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start OpenCode CLI");
        }

        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await process.WaitForExitAsync(cancellationTokenSource.Token).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var standardError = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"CLI Error: {process.ExitCode} - {standardError}");
        }

        return await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
    }

    private ProviderUsage ParseOutput(string output, ProviderConfig config)
    {
        var cleaned = CleanAnsiOutput(output);
        var totalCost = ParseValue<double>(cleaned, @"Total Cost\s+\$([0-9.]+)");
        var sessions = ParseValue<int>(cleaned, @"Sessions\s+([0-9,]+)");
        var messages = ParseValue<int>(cleaned, @"Messages\s+([0-9,]+)");
        var days = ParseValue<int>(cleaned, @"Days\s+(\d+)");
        var details = BuildDetails(cleaned);

        return CreateUsage(this.ProviderId, config, output, totalCost, sessions, messages, days, details);
    }

    private async Task<bool> IsInPathAsync(string command)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "where" : "which",
                Arguments = command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return false;
            }

            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await process.WaitForExitAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug("IsInPath check failed: {Message}", ex.Message);
            return false;
        }
    }

    private sealed class ModelUsage
    {
        public string Name { get; set; } = string.Empty;

        public int Messages { get; set; }

        public double Tokens { get; set; }

        public double Cost { get; set; }
    }

    private sealed class ToolUsage
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        public double Percentage { get; set; }
    }
}
