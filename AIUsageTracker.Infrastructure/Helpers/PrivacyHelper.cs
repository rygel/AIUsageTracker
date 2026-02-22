using System.Text.RegularExpressions;

namespace AIUsageTracker.Infrastructure.Helpers;

public static class PrivacyHelper
{
    private static readonly Regex EmailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);

    public static string MaskContent(string input, string? accountName = null)
    {
        if (string.IsNullOrEmpty(input)) return input;

        string result = input;

        // 1. Mask emails
        if (EmailRegex.IsMatch(result))
        {
            result = EmailRegex.Replace(result, match =>
            {
                var email = match.Value;
                var parts = email.Split('@');
                if (parts.Length != 2) return "*****";

                var name = parts[0];
                var domain = parts[1];
                var maskedDomain = Regex.Replace(domain, @"[^.]", "*");

                return $"{MaskString(name)}@{maskedDomain}";
            });
        }

        // 2. Surgical masking for accountName if provided
        if (!string.IsNullOrEmpty(accountName) && result.Contains(accountName))
        {
            result = result.Replace(accountName, MaskString(accountName));
        }

        // 3. If no surgical targets were found and it's JUST a string that might be sensitive (like a username itself)
        // we only do this if it was historically called as generic masking.
        // However, if we want to preserve context, we should NOT generic mask the whole string anymore.
        // In the new approach, if input == accountName, step 2 handles it.
        
        return result;
    }

    public static string MaskString(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        if (input.Length <= 2) return new string('*', input.Length);
        
        return input.Substring(0, 1) + new string('*', Math.Min(input.Length - 2, 5)) + input.Substring(input.Length - 1);
    }

    public static string MaskPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        // Try to identify the user directory to mask it specifically
        var userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(userProfile, System.StringComparison.OrdinalIgnoreCase))
        {
            var userName = System.IO.Path.GetFileName(userProfile);
            var maskedUser = MaskString(userName);
            return path.Replace(userProfile, userProfile.Replace(userName, maskedUser));
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
}

