// <copyright file="AuthSource.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public static class AuthSource
{
    public const string None = "none";
    public const string Unknown = "Unknown";
    public const string SystemDefault = "System Default";
    public const string EnvironmentPrefix = "Env";
    public const string ConfigPrefix = "Config";
    public const string RooPrefix = "Roo Code";
    public const string KiloPrefix = "Kilo Code";

    public static string FromEnvironmentVariable(string environmentVariableName)
    {
        return $"{EnvironmentPrefix}: {environmentVariableName}";
    }

    public static string FromConfigFile(string fileName)
    {
        return $"{ConfigPrefix}: {fileName}";
    }

    public static string FromRooPath(string path)
    {
        return $"{RooPrefix}: {path}";
    }

    public static bool IsEnvironment(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.StartsWith($"{EnvironmentPrefix}:", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsRooOrKilo(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains(RooPrefix, StringComparison.OrdinalIgnoreCase) ||
               value.Contains(KiloPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
