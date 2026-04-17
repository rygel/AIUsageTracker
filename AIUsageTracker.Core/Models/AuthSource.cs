// <copyright file="AuthSource.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

public static class AuthSource
{
    public const string None = "none";
    public const string Unknown = "Unknown";
    public const string SystemDefault = "System Default";
    public const string OpenCodeSession = "OpenCode Session";
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

    public static string CodexNative(string planType)
    {
        return $"Codex Native ({planType})";
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

    public static bool IsConfig(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
               value.StartsWith($"{ConfigPrefix}:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Extracts the variable name from "Env: VARIABLE_NAME".</summary>
    /// <returns></returns>
    public static bool TryParseEnvironmentVariable(string? authSource, out string varName)
    {
        var prefix = $"{EnvironmentPrefix}: ";
        if (!string.IsNullOrWhiteSpace(authSource) &&
            authSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            varName = authSource.Substring(prefix.Length).Trim();
            return true;
        }

        varName = string.Empty;
        return false;
    }

    /// <summary>
    /// Extracts all file paths from a "Config: path1, path2" auth source string.
    /// A config entry may list multiple comma-separated paths when keys were merged
    /// from several files.
    /// </summary>
    /// <returns></returns>
    public static IReadOnlyList<string> ParseConfigFilePaths(string? authSource)
    {
        if (string.IsNullOrWhiteSpace(authSource))
        {
            return Array.Empty<string>();
        }

        var prefix = $"{ConfigPrefix}: ";
        if (!authSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        var rest = authSource.Substring(prefix.Length);
        return rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>Extracts the file path from "Roo Code: /path/to/file".</summary>
    /// <returns></returns>
    public static bool TryParseRooPath(string? authSource, out string filePath)
    {
        var prefix = $"{RooPrefix}: ";
        if (!string.IsNullOrWhiteSpace(authSource) &&
            authSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            filePath = authSource.Substring(prefix.Length).Trim();
            return true;
        }

        filePath = string.Empty;
        return false;
    }
}
