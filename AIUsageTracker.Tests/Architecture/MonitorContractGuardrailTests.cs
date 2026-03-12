// <copyright file="MonitorContractGuardrailTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Architecture;

public class MonitorContractGuardrailTests
{
    [Fact]
    public void MonitorService_DoesNotDeclareNestedResponseDtos()
    {
        var monitorServicePath = GetRepoPath("AIUsageTracker.Core", "MonitorClient", "MonitorService.cs");
        var source = File.ReadAllText(monitorServicePath);

        Assert.DoesNotContain("private class ScanKeysResponse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private class CheckResponse", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorService_DoesNotUseHardcodedApiRouteLiterals()
    {
        var monitorServicePath = GetRepoPath("AIUsageTracker.Core", "MonitorClient", "MonitorService.cs");
        var source = File.ReadAllText(monitorServicePath);

        Assert.DoesNotContain("\"/api/", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorProject_DoesNotDuplicateRouteCatalog()
    {
        var duplicateRouteCatalogPath = GetRepoPath("AIUsageTracker.Monitor", "Endpoints", "MonitorApiRoutes.cs");
        Assert.False(
            File.Exists(duplicateRouteCatalogPath),
            "Route constants should be sourced from AIUsageTracker.Core.MonitorClient.MonitorApiRoutes.");
    }

    [Fact]
    public void MonitorScanKeysEndpoint_UsesSharedScanKeysResponseContract()
    {
        var endpointPath = GetRepoPath("AIUsageTracker.Monitor", "Endpoints", "MonitorConfigEndpoints.cs");
        var source = File.ReadAllText(endpointPath);

        Assert.Contains("new AgentScanKeysResponse", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorUsageEndpoints_DoNotExposeLegacyProviderCapabilitiesRoute()
    {
        var endpointPath = GetRepoPath("AIUsageTracker.Monitor", "Endpoints", "MonitorUsageEndpoints.cs");
        var source = File.ReadAllText(endpointPath);

        Assert.DoesNotContain("MonitorApiRoutes.ProviderCapabilities", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentProviderCapabilitiesSnapshot", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorOpenApi_DoesNotExposeLegacyProviderCapabilitiesRoute()
    {
        var openApiPath = GetRepoPath("AIUsageTracker.Monitor", "openapi.yaml");
        var source = File.ReadAllText(openApiPath);

        Assert.DoesNotContain("/api/providers/capabilities", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentProviderCapabilitiesSnapshot", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorClientContract_DoesNotExposeLegacyProviderCapabilitiesApi()
    {
        var monitorClientContractPath = GetRepoPath("AIUsageTracker.Core", "Interfaces", "IMonitorService.cs");
        var source = File.ReadAllText(monitorClientContractPath);

        Assert.DoesNotContain("GetProviderCapabilitiesAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GroupedUsageProjection_DoesNotUseDerivedProviderRowsAsModelFallback()
    {
        var projectionPath = GetRepoPath("AIUsageTracker.Monitor", "Services", "GroupedUsageProjectionService.cs");
        var source = File.ReadAllText(projectionPath);

        Assert.DoesNotContain("BuildModelsFromDerivedProviderRows", source, StringComparison.Ordinal);
    }

    [Fact]
    public void UsageDatabase_DoesNotApplyProviderPersistenceFiltering()
    {
        var usageDatabasePath = GetRepoPath("AIUsageTracker.Monitor", "Services", "UsageDatabase.cs");
        var source = File.ReadAllText(usageDatabasePath);

        Assert.DoesNotContain("ShouldPersistProviderId(", source, StringComparison.Ordinal);
    }

    private static string GetRepoPath(params string[] segments)
    {
        var root = GetRepoRoot();
        return Path.Combine([root, .. segments]);
    }

    private static string GetRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AIUsageTracker.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
