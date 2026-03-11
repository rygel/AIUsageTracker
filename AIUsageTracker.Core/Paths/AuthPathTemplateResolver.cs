// <copyright file="AuthPathTemplateResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Paths;

public static class AuthPathTemplateResolver
{
    public static string Resolve(string pathTemplate, string userProfileRoot)
    {
        var appDataRoot = Path.Combine(userProfileRoot, "AppData", "Roaming");
        var localAppDataRoot = Path.Combine(userProfileRoot, "AppData", "Local");

        return pathTemplate
            .Replace("%USERPROFILE%", userProfileRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", appDataRoot, StringComparison.OrdinalIgnoreCase)
            .Replace("%LOCALAPPDATA%", localAppDataRoot, StringComparison.OrdinalIgnoreCase);
    }
}
