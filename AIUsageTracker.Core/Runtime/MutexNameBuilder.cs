// <copyright file="MutexNameBuilder.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Runtime;

public static class MutexNameBuilder
{
    public static string BuildLocalName(string namePrefix, string? userName = null)
    {
        return BuildName("Local", namePrefix, userName);
    }

    public static string BuildGlobalName(string namePrefix, string? userName = null)
    {
        return BuildName("Global", namePrefix, userName);
    }

    private static string BuildName(string scope, string namePrefix, string? userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);

        var resolvedUser = string.IsNullOrWhiteSpace(userName)
            ? Environment.UserName
            : userName;
        var sanitizedUser = resolvedUser
            .Replace("\\", "_", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);

        return $@"{scope}\{namePrefix}{sanitizedUser}";
    }
}
