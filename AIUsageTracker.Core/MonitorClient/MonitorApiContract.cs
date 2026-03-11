// <copyright file="MonitorApiContract.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.MonitorClient;

public static class MonitorApiContract
{
    public const string CurrentVersion = "1";
    public const string MinimumClientVersion = CurrentVersion;

    public static readonly string[] ContractVersionJsonKeys =
    {
        "contractVersion",
        "contract_version",
        "apiContractVersion",
        "api_contract_version",
    };

    public static readonly string[] MinClientContractVersionJsonKeys =
    {
        "minClientContractVersion",
        "min_client_contract_version",
        "minClientApiContractVersion",
        "min_client_api_contract_version",
    };

    public static readonly string[] AgentVersionJsonKeys =
    {
        "agentVersion",
        "agent_version",
        "version",
    };
}
