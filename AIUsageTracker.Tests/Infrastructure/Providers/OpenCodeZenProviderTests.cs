// <copyright file="OpenCodeZenProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenCodeZenProviderTests : HttpProviderTestBase<OpenCodeZenProvider>
{
    /// <summary>
    /// Captured verbatim from <c>opencode stats --days 7 --models 10</c> on 2026-04-04.
    /// Used as a regression fixture so parsing changes are caught even in CI (no real CLI).
    /// </summary>
    private const string CapturedCliOutput = """
        ┌────────────────────────────────────────────────────────┐
        │                       OVERVIEW                         │
        ├────────────────────────────────────────────────────────┤
        │Sessions                                             16 │
        │Messages                                          1,264 │
        │Days                                                  7 │
        └────────────────────────────────────────────────────────┘

        ┌────────────────────────────────────────────────────────┐
        │                    COST & TOKENS                       │
        ├────────────────────────────────────────────────────────┤
        │Total Cost                                        $4.77 │
        │Avg Cost/Day                                      $0.68 │
        │Avg Tokens/Session                                 9.4M │
        │Median Tokens/Session                                 0 │
        │Input                                              8.4M │
        │Output                                           423.7K │
        │Cache Read                                       140.8M │
        │Cache Write                                           0 │
        └────────────────────────────────────────────────────────┘

        ┌────────────────────────────────────────────────────────┐
        │                      MODEL USAGE                       │
        ├────────────────────────────────────────────────────────┤
        │ opencode/mimo-v2-pro-free                              │
        │  Messages                                          800 │
        │  Input Tokens                                     6.9M │
        │  Output Tokens                                  302.4K │
        │  Cache Read                                     106.8M │
        │  Cache Write                                         0 │
        │  Cost                                          $0.0000 │
        ├────────────────────────────────────────────────────────┤
        │ opencode-go/kimi-k2.5                                  │
        │  Messages                                          323 │
        │  Input Tokens                                     1.4M │
        │  Output Tokens                                  167.3K │
        │  Cache Read                                      34.0M │
        │  Cache Write                                         0 │
        │  Cost                                          $4.7691 │
        ├────────────────────────────────────────────────────────┤
        │ opencode/mimo-v2-omni-free                             │
        │  Messages                                            1 │
        │  Input Tokens                                        0 │
        │  Output Tokens                                       0 │
        │  Cache Read                                          0 │
        │  Cache Write                                         0 │
        │  Cost                                          $0.0000 │
        ├────────────────────────────────────────────────────────┤
        └────────────────────────────────────────────────────────┘

        ┌────────────────────────────────────────────────────────┐
        │                      TOOL USAGE                        │
        ├────────────────────────────────────────────────────────┤
        │ bash               ████████████████████ 331 (29.2%)    │
        │ read               ███████████████      263 (23.2%)    │
        │ edit               ███████████████      254 (22.4%)    │
        │ write              █████████            150 (13.2%)    │
        │ todowrite          ███                   54 ( 4.8%)    │
        │ grep               █                     28 ( 2.5%)    │
        │ glob               █                     20 ( 1.8%)    │
        │ webfetch           █                     19 ( 1.7%)    │
        │ websearch          █                      7 ( 0.6%)    │
        │ skill              █                      3 ( 0.3%)    │
        │ codesearch         █                      2 ( 0.2%)    │
        │ invalid            █                      1 ( 0.1%)    │
        │ task               █                      1 ( 0.1%)    │
        └────────────────────────────────────────────────────────┘
        """;

    /// <summary>
    /// Minimal output: single session, zero cost, no model/tool usage sections.
    /// </summary>
    private const string MinimalCliOutput = """
        ┌────────────────────────────────────────────────────────┐
        │                       OVERVIEW                         │
        ├────────────────────────────────────────────────────────┤
        │Sessions                                              1 │
        │Messages                                              5 │
        │Days                                                  1 │
        └────────────────────────────────────────────────────────┘

        ┌────────────────────────────────────────────────────────┐
        │                    COST & TOKENS                       │
        ├────────────────────────────────────────────────────────┤
        │Total Cost                                        $0.00 │
        │Avg Cost/Day                                      $0.00 │
        │Avg Tokens/Session                                   0  │
        │Median Tokens/Session                                 0 │
        │Input                                                 0 │
        │Output                                                0 │
        │Cache Read                                            0 │
        │Cache Write                                           0 │
        └────────────────────────────────────────────────────────┘
        """;

    /// <summary>
    /// Output with large numbers (billions of tokens, high cost).
    /// </summary>
    private const string HighVolumeCliOutput = """
        ┌────────────────────────────────────────────────────────┐
        │                       OVERVIEW                         │
        ├────────────────────────────────────────────────────────┤
        │Sessions                                            512 │
        │Messages                                         42,837 │
        │Days                                                 30 │
        └────────────────────────────────────────────────────────┘

        ┌────────────────────────────────────────────────────────┐
        │                    COST & TOKENS                       │
        ├────────────────────────────────────────────────────────┤
        │Total Cost                                      $285.39 │
        │Avg Cost/Day                                      $9.51 │
        │Avg Tokens/Session                                 1.2B │
        │Median Tokens/Session                            500.0M │
        │Input                                              3.7B │
        │Output                                           912.5M │
        │Cache Read                                        42.1B │
        │Cache Write                                           0 │
        └────────────────────────────────────────────────────────┘
        """;

    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly OpenCodeZenProvider _provider;

    public OpenCodeZenProviderTests()
    {
        this._provider = new OpenCodeZenProvider(this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsTotalCost()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Equal(4.77, usage.RequestsUsed, precision: 2);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsSessions()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("16 sessions", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsMessages()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("1264 msgs", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsDays()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("7 days", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsInputTokens()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("In:8.4M", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsOutputTokens()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("Out:423.7K", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsAvgCostPerDay()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("Avg/day:$0.68", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_SetsCorrectProviderMetadata()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.True(usage.IsAvailable);
        Assert.Equal(200, usage.HttpStatus);
        Assert.True(usage.IsCurrencyUsage);
        Assert.False(usage.IsQuotaBased);
        Assert.Equal(PlanType.Usage, usage.PlanType);
        Assert.Equal("opencode-zen", usage.ProviderId);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_PreservesRawOutput()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Equal(CapturedCliOutput, usage.RawJson);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsModelUsage()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("Models:", usage.Description, StringComparison.Ordinal);
        Assert.Contains("opencode-go/kimi-k2.5", usage.Description, StringComparison.Ordinal);
        Assert.Contains("323msgs", usage.Description, StringComparison.Ordinal);
        Assert.Contains("$4.77", usage.Description, StringComparison.Ordinal);
        Assert.Contains("opencode/mimo-v2-pro-free", usage.Description, StringComparison.Ordinal);
        Assert.Contains("800msgs", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_CapturedFixture_ExtractsToolUsage()
    {
        var usage = this.InvokeParseOutput(CapturedCliOutput);

        Assert.Contains("Tools:", usage.Description, StringComparison.Ordinal);
        Assert.Contains("bash:331", usage.Description, StringComparison.Ordinal);
        Assert.Contains("read:263", usage.Description, StringComparison.Ordinal);
        Assert.Contains("edit:254", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_MinimalOutput_ParsesZeroCostCorrectly()
    {
        var usage = this.InvokeParseOutput(MinimalCliOutput);

        Assert.True(usage.IsAvailable);
        Assert.Equal(0.0, usage.RequestsUsed);
        Assert.Contains("1 sessions", usage.Description, StringComparison.Ordinal);
        Assert.Contains("5 msgs", usage.Description, StringComparison.Ordinal);
        Assert.Contains("1 days", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_HighVolumeOutput_ParsesLargeNumbers()
    {
        var usage = this.InvokeParseOutput(HighVolumeCliOutput);

        Assert.Equal(285.39, usage.RequestsUsed, precision: 2);
        Assert.Contains("512 sessions", usage.Description, StringComparison.Ordinal);
        Assert.Contains("42837 msgs", usage.Description, StringComparison.Ordinal);
        Assert.Contains("30 days", usage.Description, StringComparison.Ordinal);
        Assert.Contains("In:3.7B", usage.Description, StringComparison.Ordinal);
        Assert.Contains("Out:912.5M", usage.Description, StringComparison.Ordinal);
        Assert.Contains("Avg/day:$9.51", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseOutput_OutputWithAnsiEscapeCodes_StripsCodesAndParses()
    {
        // Simulate ANSI codes that terminals may emit (colors, cursor movement).
        var ansiOutput = "\u001b[1;32m" + CapturedCliOutput.Replace(
            "Total Cost",
            "\u001b[0mTotal Cost\u001b[K",
            StringComparison.Ordinal) + "\u001b[0m";

        var usage = this.InvokeParseOutput(ansiOutput);

        Assert.True(usage.IsAvailable);
        Assert.Equal(4.77, usage.RequestsUsed, precision: 2);
        Assert.Contains("16 sessions", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_MockCli_ReturnsCorrectUsageEndToEndAsync()
    {
        // End-to-end: create a mock CLI script that outputs the captured fixture,
        // then run the full GetUsageAsync pipeline against it.
        var (scriptPath, tempDir) = CreateMockCliScript(CapturedCliOutput);
        try
        {
            var provider = new OpenCodeZenProvider(this.Logger.Object, scriptPath);
            var result = await provider.GetUsageAsync(this.Config);
            var usage = result.Single();

            Assert.True(usage.IsAvailable, $"Expected available but got: {usage.Description}");
            Assert.Equal(200, usage.HttpStatus);
            Assert.Equal(4.77, usage.RequestsUsed, precision: 2);
            Assert.True(usage.IsCurrencyUsage);
            Assert.Contains("16 sessions", usage.Description, StringComparison.Ordinal);
            Assert.Contains("1264 msgs", usage.Description, StringComparison.Ordinal);
            Assert.Contains("7 days", usage.Description, StringComparison.Ordinal);
            Assert.Contains("In:8.4M", usage.Description, StringComparison.Ordinal);
            Assert.Contains("Out:423.7K", usage.Description, StringComparison.Ordinal);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_MockCli_MinimalOutput_ReturnsZeroCostEndToEndAsync()
    {
        var (scriptPath, tempDir) = CreateMockCliScript(MinimalCliOutput);
        try
        {
            var provider = new OpenCodeZenProvider(this.Logger.Object, scriptPath);
            var result = await provider.GetUsageAsync(this.Config);
            var usage = result.Single();

            Assert.True(usage.IsAvailable);
            Assert.Equal(0.0, usage.RequestsUsed);
            Assert.Contains("1 sessions", usage.Description, StringComparison.Ordinal);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_MockCli_HighVolume_ParsesLargeNumbersEndToEndAsync()
    {
        var (scriptPath, tempDir) = CreateMockCliScript(HighVolumeCliOutput);
        try
        {
            var provider = new OpenCodeZenProvider(this.Logger.Object, scriptPath);
            var result = await provider.GetUsageAsync(this.Config);
            var usage = result.Single();

            Assert.True(usage.IsAvailable);
            Assert.Equal(285.39, usage.RequestsUsed, precision: 2);
            Assert.Contains("512 sessions", usage.Description, StringComparison.Ordinal);
            Assert.Contains("In:3.7B", usage.Description, StringComparison.Ordinal);
        }
        finally
        {
            CleanupTempDir(tempDir);
        }
    }

    [Fact]
    public async Task GetUsageAsync_CliNotFound_ReturnsUnavailableAsync()
    {
        var provider = new OpenCodeZenProvider(this.Logger.Object, "non-existent-cli");

        var result = await provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(404, usage.HttpStatus);
        Assert.Contains("CLI not found", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_CliTimeout_ReturnsUnavailableWithTimeoutMessageAsync()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = OperatingSystem.IsWindows()
            ? Path.Combine(tempDirectory, "slow-opencode.cmd")
            : Path.Combine(tempDirectory, "slow-opencode.sh");

        if (OperatingSystem.IsWindows())
        {
            await File.WriteAllTextAsync(
                scriptPath,
                "@echo off\r\nping -n 30 127.0.0.1 >nul\r\necho delayed\r\n",
                CancellationToken.None);
        }
        else
        {
            await File.WriteAllTextAsync(
                scriptPath,
                "#!/usr/bin/env bash\nsleep 30\necho delayed\n",
                CancellationToken.None);
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute);
        }

        try
        {
            var provider = new OpenCodeZenProvider(this.Logger.Object, scriptPath, TimeSpan.FromMilliseconds(250));

            var result = await provider.GetUsageAsync(this.Config);

            var usage = result.Single();
            Assert.False(usage.IsAvailable);
            Assert.Equal(500, usage.HttpStatus);
            Assert.Contains("timed out", usage.Description, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempDir(tempDirectory);
        }
    }

    private ProviderUsage InvokeParseOutput(string output)
    {
        var parseOutput = typeof(OpenCodeZenProvider).GetMethod(
            "ParseOutput", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(parseOutput);
        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName("opencode-zen");
        return (ProviderUsage)parseOutput.Invoke(this._provider, new object[] { output, this.Config, providerLabel })!;
    }

    private static (string ScriptPath, string TempDir) CreateMockCliScript(string output)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "opencode-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        if (OperatingSystem.IsWindows())
        {
            var dataFile = Path.Combine(tempDir, "fixture.txt");
            File.WriteAllText(dataFile, output);
            var scriptPath = Path.Combine(tempDir, "mock-opencode.cmd");
            File.WriteAllText(scriptPath, $"@type \"{dataFile}\"\r\n");
            return (scriptPath, tempDir);
        }
        else
        {
            var scriptPath = Path.Combine(tempDir, "mock-opencode.sh");
            var escaped = output.Replace("'", "'\\''", StringComparison.Ordinal);
            File.WriteAllText(scriptPath, $"#!/usr/bin/env bash\ncat <<'FIXTURE_EOF'\n{output}\nFIXTURE_EOF\n");
            File.SetUnixFileMode(
                scriptPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            return (scriptPath, tempDir);
        }
    }

    private static void CleanupTempDir(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temporary test artifacts.
        }
    }
}
