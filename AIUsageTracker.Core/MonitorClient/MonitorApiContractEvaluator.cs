// <copyright file="MonitorApiContractEvaluator.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;

namespace AIUsageTracker.Core.MonitorClient;

public static class MonitorApiContractEvaluator
{
    public static AgentContractHandshakeResult Evaluate(
        string? contractVersion,
        string? minClientContractVersion,
        string? reportedAgentVersion,
        string expectedClientContractVersion)
    {
        if (string.IsNullOrWhiteSpace(contractVersion))
        {
            return new AgentContractHandshakeResult
            {
                IsReachable = true,
                IsCompatible = false,
                AgentVersion = reportedAgentVersion,
                MinClientContractVersion = minClientContractVersion,
                Message = $"Agent API contract version is missing (expected {expectedClientContractVersion}).",
            };
        }

        var isCompatible = IsContractCompatible(
            contractVersion,
            minClientContractVersion,
            expectedClientContractVersion,
            out var incompatibilityReason);
        if (isCompatible)
        {
            return new AgentContractHandshakeResult
            {
                IsReachable = true,
                IsCompatible = true,
                AgentContractVersion = contractVersion,
                MinClientContractVersion = minClientContractVersion,
                AgentVersion = reportedAgentVersion,
                Message = "Agent API contract is compatible.",
            };
        }

        var versionSuffix = string.IsNullOrWhiteSpace(reportedAgentVersion)
            ? string.Empty
            : $" (agent {reportedAgentVersion})";
        return new AgentContractHandshakeResult
        {
            IsReachable = true,
            IsCompatible = false,
            AgentContractVersion = contractVersion,
            MinClientContractVersion = minClientContractVersion,
            AgentVersion = reportedAgentVersion,
            Message = incompatibilityReason ??
                $"Agent API contract mismatch: expected {expectedClientContractVersion}, got {contractVersion}{versionSuffix}.",
        };
    }

    private static bool IsContractCompatible(
        string monitorContractVersion,
        string? minClientContractVersion,
        string clientContractVersion,
        out string? incompatibilityReason)
    {
        incompatibilityReason = null;
        if (!TryParseContractVersion(monitorContractVersion, out var monitorContract))
        {
            incompatibilityReason = $"Agent API contract version '{monitorContractVersion}' is invalid.";
            return false;
        }

        if (!TryParseContractVersion(clientContractVersion, out var clientContract))
        {
            incompatibilityReason = $"Client API contract version '{clientContractVersion}' is invalid.";
            return false;
        }

        if (monitorContract.Major != clientContract.Major)
        {
            incompatibilityReason =
                $"Agent API contract major mismatch: expected major {clientContract.Major.ToString(CultureInfo.InvariantCulture)}, got {monitorContract.Major.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(minClientContractVersion))
        {
            return true;
        }

        if (!TryParseContractVersion(minClientContractVersion, out var minClientContract))
        {
            incompatibilityReason =
                $"Agent minimum client contract version '{minClientContractVersion}' is invalid.";
            return false;
        }

        if (CompareContractVersions(clientContract, minClientContract) < 0)
        {
            incompatibilityReason =
                $"Agent requires client contract >= {minClientContractVersion}, current is {clientContractVersion}.";
            return false;
        }

        return true;
    }

    private static bool TryParseContractVersion(string value, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[1..];
        }

        if (normalized.IndexOf('.', StringComparison.Ordinal) < 0)
        {
            if (!int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var majorOnly))
            {
                return false;
            }

            version = new Version(majorOnly, 0);
            return true;
        }

        if (!Version.TryParse(normalized, out var parsedVersion))
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private static int CompareContractVersions(Version left, Version right)
    {
        var leftBuild = left.Build < 0 ? 0 : left.Build;
        var rightBuild = right.Build < 0 ? 0 : right.Build;
        var leftRevision = left.Revision < 0 ? 0 : left.Revision;
        var rightRevision = right.Revision < 0 ? 0 : right.Revision;

        if (left.Major != right.Major)
        {
            return left.Major.CompareTo(right.Major);
        }

        if (left.Minor != right.Minor)
        {
            return left.Minor.CompareTo(right.Minor);
        }

        if (leftBuild != rightBuild)
        {
            return leftBuild.CompareTo(rightBuild);
        }

        return leftRevision.CompareTo(rightRevision);
    }
}
