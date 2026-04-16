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
    private const string ProviderDisplayName = "OpenCode";
    private const string DefaultCliCommand = "opencode";
    private static readonly TimeSpan DefaultCliTimeout = TimeSpan.FromSeconds(20);
    private static readonly Regex[] CleanupPatterns =
    {
        new("\u001b\\[[0-9;]*m", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
        new("\u001b\\[K", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
        new("[0-9]+A", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
        new("\u001b\\[[0-9]*;[0-9]*m", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1)),
    };

    private static readonly Regex SeparatorRegex = new(
        @"[─━]{10,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1));

    private static readonly Regex ToolUsageLineRegex = new(
        @"(?<tool>[\w.-]+)\s+[█░▓▒]+\s*(?<count>[0-9]+)\s+\(\s*(?<percentage>[\d.]+)%\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.NonBacktracking,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Common installation paths for the opencode CLI, checked when PATH discovery fails.
    /// Mirrors the fallback strategy from opencode-bar/scripts/query-opencode.sh.
    /// </summary>
    private static readonly string[] FallbackPaths = OperatingSystem.IsWindows()
        ? new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "opencode.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "npm", "opencode.cmd"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "bin", "opencode.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "opencode.exe"),
        }
        : new[]
        {
            "/opt/homebrew/bin/opencode",
            "/usr/local/bin/opencode",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "bin", "opencode"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "opencode"),
            "/usr/bin/opencode",
        };

    private readonly ILogger<OpenCodeZenProvider> _logger;
    private readonly TimeSpan _cliTimeout;
    private readonly string? _cliPathOverride;

    public OpenCodeZenProvider(ILogger<OpenCodeZenProvider> logger)
    {
        this._logger = logger;
        this._cliTimeout = DefaultCliTimeout;
    }

    public OpenCodeZenProvider(ILogger<OpenCodeZenProvider> logger, string cliPath, TimeSpan? cliTimeout = null)
        : this(logger)
    {
        this._cliPathOverride = cliPath;
        this._cliTimeout = cliTimeout ?? this._cliTimeout;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "opencode-zen",
        ProviderDisplayName,
        PlanType.Usage,
        isQuotaBased: false)
    {
        SettingsMode = ProviderSettingsMode.AutoDetectedStatus,
        IsTooltipOnly = true,
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var cliPath = await this.ResolveCliPathAsync().ConfigureAwait(false);
        if (cliPath == null)
        {
            return new[]
            {
                CreateUnavailableUsage(
                    this.ProviderId,
                    "CLI not found — install opencode or add it to PATH",
                    config.AuthSource,
                    "Searched: PATH, fallback paths: " + string.Join(", ", FallbackPaths),
                    404,
                    ProviderUsageState.Missing),
            };
        }

        try
        {
            var output = await this.RunCliAsync(cliPath).ConfigureAwait(false);
            return new[] { this.ParseOutput(output, config) };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or TimeoutException)
        {
            this._logger.LogWarning(ex, "OpenCode CLI failed: {Message}", ex.Message);
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
        int httpStatus,
        ProviderUsageState state = ProviderUsageState.Error)
    {
        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = ProviderDisplayName,
            IsAvailable = false,
            Description = description,
            State = state,
            IsQuotaBased = false,
            PlanType = PlanType.Usage,
            AuthSource = authSource ?? string.Empty,
            RawJson = rawJson,
            HttpStatus = httpStatus,
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

    private static double ExtractTokenCount(string input, string pattern)
    {
        var match = Regex.Match(
            input,
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeSpan.FromSeconds(1));
        return match.Success && match.Groups.Count > 1
            ? ParseTokenCount(match.Groups[1].Value)
            : 0;
    }

    private static T ParseValue<T>(string input, string pattern)
        where T : struct
    {
        var match = Regex.Match(
            input,
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
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

    private static List<ModelUsageEntry> ParseModelUsage(string cleaned)
    {
        var results = new List<ModelUsageEntry>();
        var sections = SeparatorRegex.Split(cleaned);

        // Find MODEL USAGE section — blocks between the header and the next major section
        var inModelSection = false;
        foreach (var section in sections)
        {
            if (section.Contains("MODEL USAGE", StringComparison.Ordinal))
            {
                inModelSection = true;
                continue;
            }

            if (inModelSection && (section.Contains("TOOL USAGE", StringComparison.Ordinal) ||
                                   section.Contains("OVERVIEW", StringComparison.Ordinal) ||
                                   section.Contains("COST & TOKENS", StringComparison.Ordinal)))
            {
                break;
            }

            if (!inModelSection)
            {
                continue;
            }

            // Each block is one model — extract fields line by line
            var lines = section.Split('\n')
                .Select(l => StripBoxDrawingChars(l).Trim())
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            if (lines.Count == 0)
            {
                continue;
            }

            // First line is model name (e.g. "opencode-go/kimi-k2.5")
            var modelName = lines[0].Trim();
            if (string.IsNullOrWhiteSpace(modelName) ||
                modelName.Contains("MODEL USAGE", StringComparison.Ordinal))
            {
                continue;
            }

            var entry = new ModelUsageEntry { Name = modelName };
            foreach (var line in lines.Skip(1))
            {
                if (line.StartsWith("Messages", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Messages = ParseValue<int>(line, @"Messages\s+([0-9,]+)");
                }
                else if (line.StartsWith("Input Tokens", StringComparison.OrdinalIgnoreCase))
                {
                    entry.InputTokens = ExtractTokenCount(line, @"Input Tokens\s+([0-9.,KMBT]+)");
                }
                else if (line.StartsWith("Output Tokens", StringComparison.OrdinalIgnoreCase))
                {
                    entry.OutputTokens = ExtractTokenCount(line, @"Output Tokens\s+([0-9.,KMBT]+)");
                }
                else if (line.StartsWith("Cache Read", StringComparison.OrdinalIgnoreCase))
                {
                    entry.CacheReadTokens = ExtractTokenCount(line, @"Cache Read\s+([0-9.,KMBT]+)");
                }
                else if (line.StartsWith("Cost", StringComparison.OrdinalIgnoreCase))
                {
                    entry.Cost = ParseValue<double>(line, @"Cost\s+\$([0-9.]+)");
                }
            }

            if (!string.IsNullOrWhiteSpace(entry.Name))
            {
                results.Add(entry);
            }
        }

        return results.OrderByDescending(m => m.Cost).ThenByDescending(m => m.Messages).ToList();
    }

    private static List<ToolUsageEntry> ParseToolUsage(string cleaned)
    {
        var sections = SeparatorRegex.Split(cleaned);
        var toolSection = sections
            .SkipWhile(block => !block.Contains("TOOL USAGE", StringComparison.Ordinal))
            .Skip(1)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(toolSection))
        {
            return new List<ToolUsageEntry>();
        }

        // Parse line by line — tool lines contain bar chars followed by count and percentage
        var results = new List<ToolUsageEntry>();
        foreach (var rawLine in toolSection.Split('\n'))
        {
            var line = StripBoxDrawingChars(rawLine).Trim();
            var match = ToolUsageLineRegex.Match(line);
            if (match.Success)
            {
                results.Add(new ToolUsageEntry
                {
                    Name = match.Groups["tool"].Value,
                    Count = int.Parse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture),
                    Percentage = double.Parse(match.Groups["percentage"].Value, NumberStyles.Float, CultureInfo.InvariantCulture),
                });
            }
        }

        return results.OrderByDescending(t => t.Count).ToList();
    }

    private static string BuildDescription(
        double totalCost,
        int sessions,
        int messages,
        int days,
        double inputTokens,
        double outputTokens,
        double avgCostPerDay,
        List<ModelUsageEntry> models,
        List<ToolUsageEntry> tools)
    {
        var parts = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture, $"${totalCost:F2} ({sessions} sessions, {messages} msgs, {days} days)"),
        };

        // Token summary
        var tokenParts = new List<string>();
        if (inputTokens > 0)
        {
            tokenParts.Add($"In:{FormatTokens(inputTokens)}");
        }

        if (outputTokens > 0)
        {
            tokenParts.Add($"Out:{FormatTokens(outputTokens)}");
        }

        if (avgCostPerDay > 0)
        {
            tokenParts.Add(string.Create(CultureInfo.InvariantCulture, $"Avg/day:${avgCostPerDay:F2}"));
        }

        if (tokenParts.Count > 0)
        {
            parts.Add(string.Join(" | ", tokenParts));
        }

        // Model breakdown (top 3)
        if (models.Count > 0)
        {
            var modelSummaries = models.Take(3).Select(m =>
            {
                var costStr = m.Cost > 0
                    ? string.Create(CultureInfo.InvariantCulture, $" ${m.Cost:F2}")
                    : string.Empty;
                return $"{m.Name} ({m.Messages}msgs{costStr})";
            });
            parts.Add("Models: " + string.Join(", ", modelSummaries));
        }

        // Tool summary (top 5)
        if (tools.Count > 0)
        {
            var toolSummaries = tools.Take(5).Select(t => $"{t.Name}:{t.Count}");
            parts.Add("Tools: " + string.Join(" ", toolSummaries));
        }

        return string.Join(" | ", parts);
    }

    private static string StripBoxDrawingChars(string input)
    {
        return new string(input.Where(c => c < '\u2500' || c > '\u257F').ToArray());
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

    private async Task<string?> ResolveCliPathAsync()
    {
        // If an explicit path was set (e.g. for testing), use it directly
        if (!string.IsNullOrEmpty(this._cliPathOverride))
        {
            return File.Exists(this._cliPathOverride) ? this._cliPathOverride : null;
        }

        // Strategy 1: Check if opencode is in PATH
        if (await this.IsInPathAsync(DefaultCliCommand).ConfigureAwait(false))
        {
            var resolved = await this.ResolvePathLocationAsync(DefaultCliCommand).ConfigureAwait(false);
            if (resolved != null)
            {
                this._logger.LogDebug("Found opencode via PATH: {Path}", resolved);
                return resolved;
            }

            return DefaultCliCommand;
        }

        // Strategy 2: Try via login shell (macOS/Linux — picks up paths from .zshrc/.bashrc)
        if (!OperatingSystem.IsWindows())
        {
            var loginShellPath = await this.ResolveViaLoginShellAsync().ConfigureAwait(false);
            if (loginShellPath != null)
            {
                this._logger.LogDebug("Found opencode via login shell: {Path}", loginShellPath);
                return loginShellPath;
            }
        }

        // Strategy 3: Check common fallback installation paths
        var found = FallbackPaths.FirstOrDefault(File.Exists);
        if (found != null)
        {
            this._logger.LogDebug("Found opencode via fallback path: {Path}", found);
            return found;
        }

        return null;
    }

    private async Task<string> RunCliAsync(string cliPath)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = "stats --days 7 --models 10 --tools 10",
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

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        using var cancellationTokenSource = new CancellationTokenSource(this._cliTimeout);
        try
        {
            await process.WaitForExitAsync(cancellationTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                this._logger.LogDebug(ex, "Failed to kill timed-out OpenCode CLI process");
            }

            throw new TimeoutException($"OpenCode CLI timed out after {this._cliTimeout.TotalSeconds:F0}s");
        }

        var standardError = await standardErrorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"CLI Error: {process.ExitCode} - {standardError}");
        }

        return await standardOutputTask.ConfigureAwait(false);
    }

    private ProviderUsage ParseOutput(string output, ProviderConfig config)
    {
        var cleaned = CleanAnsiOutput(output);

        // Overview
        var totalCost = ParseValue<double>(cleaned, @"Total Cost\s+\$([0-9.]+)");
        var sessions = ParseValue<int>(cleaned, @"Sessions\s+([0-9,]+)");
        var messages = ParseValue<int>(cleaned, @"Messages\s+([0-9,]+)");
        var days = ParseValue<int>(cleaned, @"Days\s+(\d+)");

        // Token summary
        // Use [0-9.,KMB] without T — "Input Tokens" in model blocks won't match because
        // 'T' in "Tokens" isn't in the char class, so only the summary "Input 8.4M" matches.
        var inputTokens = ExtractTokenCount(cleaned, @"Input\s+([0-9.,KMB]+)");
        var outputTokens = ExtractTokenCount(cleaned, @"Output\s+([0-9.,KMB]+)");
        var avgCostPerDay = ParseValue<double>(cleaned, @"Avg Cost/Day\s+\$([0-9.]+)");

        // Breakdowns
        var models = ParseModelUsage(cleaned);
        var tools = ParseToolUsage(cleaned);

        var description = BuildDescription(
            totalCost, sessions, messages, days,
            inputTokens, outputTokens, avgCostPerDay,
            models, tools);

        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = ProviderDisplayName,
            UsedPercent = 0.0,
            RequestsUsed = totalCost,
            RequestsAvailable = 0.0,
            IsCurrencyUsage = true,
            IsQuotaBased = false,
            PlanType = PlanType.Usage,
            IsAvailable = true,
            Description = description,
            AuthSource = config.AuthSource,
            RawJson = output,
            HttpStatus = 200,
        };
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
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            this._logger.LogDebug(ex, "IsInPath check failed: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Strategy 2 from opencode-bar/scripts/query-opencode.sh: try the user's login shell
    /// to pick up PATH entries from .zshrc/.bashrc that non-login processes don't inherit.
    /// </summary>
    private async Task<string?> ResolveViaLoginShellAsync()
    {
        try
        {
            var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/zsh";
            var processStartInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = "-lc \"which opencode 2>/dev/null\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var resolved = output.Trim();
                if (File.Exists(resolved))
                {
                    return resolved;
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            this._logger.LogDebug(ex, "Login shell discovery failed: {Message}", ex.Message);
        }

        return null;
    }

    private async Task<string?> ResolvePathLocationAsync(string command)
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
                return null;
            }

            // On Windows, `where` returns multiple lines (e.g. opencode, opencode.cmd).
            // The extensionless file is a bash shim that Process.Start can't execute.
            // Pick the .cmd or .exe variant; fall back to first line on non-Windows.
            var allOutput = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(cancellationTokenSource.Token).ConfigureAwait(false);

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(allOutput))
            {
                return null;
            }

            var lines = allOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (OperatingSystem.IsWindows())
            {
                // Prefer .cmd then .exe — these are executable via Process.Start
                var executable = lines.FirstOrDefault(l =>
                    l.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
                    l.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                return executable ?? lines.FirstOrDefault();
            }

            return lines.FirstOrDefault();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    internal sealed class ModelUsageEntry
    {
        public string Name { get; set; } = string.Empty;

        public int Messages { get; set; }

        public double InputTokens { get; set; }

        public double OutputTokens { get; set; }

        public double CacheReadTokens { get; set; }

        public double Cost { get; set; }
    }

    internal sealed class ToolUsageEntry
    {
        public string Name { get; set; } = string.Empty;

        public int Count { get; set; }

        public double Percentage { get; set; }
    }
}
