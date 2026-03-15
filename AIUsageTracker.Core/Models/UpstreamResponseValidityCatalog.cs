// <copyright file="UpstreamResponseValidityCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public static class UpstreamResponseValidityCatalog
{
    public static (UpstreamResponseValidity Validity, string Note) Evaluate(ProviderUsage usage)
    {
        if (usage.UpstreamResponseValidity != UpstreamResponseValidity.Unknown)
        {
            return (
                usage.UpstreamResponseValidity,
                string.IsNullOrWhiteSpace(usage.UpstreamResponseNote)
                    ? GetDefaultNote(usage.UpstreamResponseValidity, usage.HttpStatus)
                    : usage.UpstreamResponseNote);
        }

        var description = usage.Description ?? string.Empty;

        // Typed state short-circuits before any description heuristics
        if (usage.State == ProviderUsageState.Missing || usage.State == ProviderUsageState.Unavailable)
        {
            return (UpstreamResponseValidity.NotAttempted, "Upstream call was not attempted");
        }

        if (usage.State == ProviderUsageState.Error)
        {
            return (UpstreamResponseValidity.Invalid, "Provider reported an error");
        }

        var hasHttpStatus = usage.HttpStatus is >= 100 and <= 599;
        if (hasHttpStatus)
        {
            if (usage.HttpStatus is >= 200 and <= 299 && !LooksLikeInvalidPayload(description))
            {
                return (UpstreamResponseValidity.Valid, $"HTTP {usage.HttpStatus}");
            }

            return (UpstreamResponseValidity.Invalid, $"HTTP {usage.HttpStatus}");
        }

        if (!string.IsNullOrWhiteSpace(usage.RawJson))
        {
            if (LooksLikeInvalidPayload(description))
            {
                return (UpstreamResponseValidity.Invalid, "Captured payload failed validation");
            }

            return usage.IsAvailable
                ? (UpstreamResponseValidity.Valid, "Payload captured (no HTTP status)")
                : (UpstreamResponseValidity.Invalid, "Payload captured but usage is unavailable");
        }

        if (LooksLikeNotAttempted(description))
        {
            return (UpstreamResponseValidity.NotAttempted, "Upstream call was not attempted");
        }

        return usage.IsAvailable
            ? (UpstreamResponseValidity.Unknown, "No upstream validation metadata")
            : (UpstreamResponseValidity.NotAttempted, "Unavailable without upstream response metadata");
    }

    private static bool LooksLikeInvalidPayload(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        return description.Contains("Invalid detail contract", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("Failed to parse", StringComparison.OrdinalIgnoreCase) ||
               description.StartsWith("[Error]", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNotAttempted(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        return description.Contains("API Key", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("configured", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("not authenticated", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("not running", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("waiting", StringComparison.OrdinalIgnoreCase) ||
               description.Contains("provider integration missing", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultNote(UpstreamResponseValidity validity, int httpStatus)
    {
        return validity switch
        {
            UpstreamResponseValidity.Valid when httpStatus is >= 100 and <= 599 => $"HTTP {httpStatus}",
            UpstreamResponseValidity.Invalid when httpStatus is >= 100 and <= 599 => $"HTTP {httpStatus}",
            UpstreamResponseValidity.NotAttempted => "Upstream call was not attempted",
            UpstreamResponseValidity.Valid => "Upstream response valid",
            UpstreamResponseValidity.Invalid => "Upstream response invalid",
            _ => "Unknown upstream response validity",
        };
    }
}
