// <copyright file="PrivacyHelper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.RegularExpressions;

namespace AIUsageTracker.Infrastructure.Helpers;

public static partial class PrivacyHelper
{
    public static string MaskContent(string input, string? accountName = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        string result = input;

        // 1. Mask emails
        if (EmailRegex().IsMatch(result))
        {
            result = EmailRegex().Replace(result, match =>
            {
                var email = match.Value;
                var parts = email.Split('@');
                if (parts.Length != 2)
                {
                    return "*****";
                }

                var name = parts[0];
                var domain = parts[1];
                var maskedDomain = new string(domain.Select(ch => ch == '.' ? '.' : '*').ToArray());

                return $"{MaskString(name)}@{maskedDomain}";
            });
        }

        // 2. Surgical masking for accountName if provided
        if (!string.IsNullOrEmpty(accountName) && result.Contains(accountName, StringComparison.Ordinal))
        {
            result = result.Replace(accountName, MaskString(accountName), StringComparison.Ordinal);
        }

        // 3. If no surgical targets were found and it's JUST a string that might be sensitive (like a username itself)
        // we only do this if it was historically called as generic masking.
        // However, if we want to preserve context, we should NOT generic mask the whole string anymore.
        // In the new approach, if input == accountName, step 2 handles it.
        return result;
    }

    public static string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        if (input.Length <= 2)
        {
            return new string('*', input.Length);
        }

        return string.Concat(input.AsSpan(0, 1), new string('*', Math.Min(input.Length - 2, 5)).AsSpan(), input.AsSpan(input.Length - 1));
    }

    public static string MaskPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        // Try to identify the user directory to mask it specifically
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(userProfile, System.StringComparison.OrdinalIgnoreCase))
        {
            var userName = System.IO.Path.GetFileName(userProfile);
            var maskedUser = MaskString(userName);
            return path.Replace(userProfile, userProfile.Replace(userName, maskedUser, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        // Fallback: generic mask but keep filename
        var fileName = System.IO.Path.GetFileName(path);
        var dirName = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dirName))
        {
            return MaskString(dirName) + System.IO.Path.DirectorySeparatorChar + fileName;
        }

        return MaskString(path);
    }

    /// <summary>
    /// Masks an account identifier. If it looks like an email, the local and domain parts
    /// are masked separately (preserving dots in the domain). Otherwise falls back to <see cref="MaskString"/>.
    /// </summary>
    public static string MaskAccountIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var atIndex = input.IndexOf("@", StringComparison.Ordinal);
        if (atIndex > 0 && atIndex < input.Length - 1)
        {
            var localPart = input[..atIndex];
            var domainPart = input[(atIndex + 1)..];
            var maskedDomainChars = domainPart.ToCharArray();
            for (var i = 0; i < maskedDomainChars.Length; i++)
            {
                if (maskedDomainChars[i] != '.')
                {
                    maskedDomainChars[i] = '*';
                }
            }

            var maskedDomain = new string(maskedDomainChars);
            if (localPart.Length <= 2)
            {
                return $"{new string('*', localPart.Length)}@{maskedDomain}";
            }

            return $"{localPart[0]}{new string('*', localPart.Length - 2)}{localPart[^1]}@{maskedDomain}";
        }

        return MaskString(input);
    }

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex EmailRegex();
}
