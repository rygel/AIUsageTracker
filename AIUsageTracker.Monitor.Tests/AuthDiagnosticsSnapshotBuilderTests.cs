// <copyright file="AuthDiagnosticsSnapshotBuilderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Monitor.Services;

namespace AIUsageTracker.Monitor.Tests;

public class AuthDiagnosticsSnapshotBuilderTests
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    [Fact]
    public void Build_WhenAuthSourceUsesFilePath_ExtractsFallbackAndAgeBucket()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.SetLastWriteTimeUtc(tempFile, DateTime.UtcNow.AddMinutes(-30));
            var config = new ProviderConfig
            {
                ProviderId = "gemini-cli",
                ApiKey = TestApiKey,
                AuthSource = $"Config: {tempFile}",
                Description = "Gemini user account",
            };

            var snapshot = AuthDiagnosticsSnapshotBuilder.Build(config, DateTimeOffset.UtcNow);

            Assert.Equal("gemini-cli", snapshot.ProviderId);
            Assert.True(snapshot.Configured);
            Assert.Equal($"Config: {Path.GetFileName(tempFile)}", snapshot.AuthSource);
            Assert.Equal(Path.GetFileName(tempFile), snapshot.FallbackPathUsed);
            Assert.Equal("lt-1h", snapshot.TokenAgeBucket);
            Assert.True(snapshot.HasUserIdentity);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Build_WhenAuthSourceIsEnvironmentVariable_UsesRuntimeBucketAndNoPath()
    {
        var config = new ProviderConfig
        {
            ProviderId = "deepseek",
            ApiKey = TestApiKey,
            AuthSource = "Env: DEEPSEEK_API_KEY",
        };

        var snapshot = AuthDiagnosticsSnapshotBuilder.Build(config, DateTimeOffset.UtcNow);

        Assert.Equal("n/a", snapshot.FallbackPathUsed);
        Assert.Equal("runtime-env", snapshot.TokenAgeBucket);
        Assert.False(snapshot.HasUserIdentity);
    }

    [Fact]
    public void Build_WhenPathIsMissing_UsesMissingTokenAgeBucket()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        var config = new ProviderConfig
        {
            ProviderId = "codex",
            ApiKey = TestApiKey,
            AuthSource = $"Config: {missingPath}",
        };

        var snapshot = AuthDiagnosticsSnapshotBuilder.Build(config, DateTimeOffset.UtcNow);

        Assert.Equal(Path.GetFileName(missingPath), snapshot.FallbackPathUsed);
        Assert.Equal("missing", snapshot.TokenAgeBucket);
    }

    [Fact]
    public void Build_WhenAuthSourceContainsInlineEnvironmentValue_RedactsValueFromDiagnostics()
    {
        var config = new ProviderConfig
        {
            ProviderId = "openai",
            ApiKey = TestApiKey,
            AuthSource = "Env: OPENAI_API_KEY=sk-super-secret-value",
        };

        var snapshot = AuthDiagnosticsSnapshotBuilder.Build(config, DateTimeOffset.UtcNow);

        Assert.Equal("Env: OPENAI_API_KEY", snapshot.AuthSource);
        Assert.Equal("n/a", snapshot.FallbackPathUsed);
        Assert.Equal("runtime-env", snapshot.TokenAgeBucket);
    }

    [Fact]
    public void Build_WhenProviderSupportsAccountIdentity_MarksUserIdentityWithoutAuthSourceHeuristics()
    {
        var config = new ProviderConfig
        {
            ProviderId = "github-copilot",
            ApiKey = TestApiKey,
            AuthSource = "Config: auth.json",
        };

        var snapshot = AuthDiagnosticsSnapshotBuilder.Build(config, DateTimeOffset.UtcNow);

        Assert.True(snapshot.HasUserIdentity);
    }

    [Fact]
    public void Build_WhenProviderDoesNotSupportAccountIdentity_LeavesUserIdentityFalseWithoutDescription()
    {
        var config = new ProviderConfig
        {
            ProviderId = "deepseek",
            ApiKey = TestApiKey,
            AuthSource = "Config: auth.json",
        };

        var snapshot = AuthDiagnosticsSnapshotBuilder.Build(config, DateTimeOffset.UtcNow);

        Assert.False(snapshot.HasUserIdentity);
    }
}
