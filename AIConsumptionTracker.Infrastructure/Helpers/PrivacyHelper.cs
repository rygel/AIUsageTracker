using System.Text.RegularExpressions;

namespace AIConsumptionTracker.Infrastructure.Helpers;

public static class PrivacyHelper
{
    private static readonly Regex EmailRegex = new Regex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}", RegexOptions.Compiled);

    public static string MaskContent(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return EmailRegex.Replace(input, match =>
        {
            var email = match.Value;
            var parts = email.Split('@');
            if (parts.Length != 2) return "*****";

            var name = parts[0];
            var domain = parts[1];

            var maskedName = name.Length > 2 
                ? name.Substring(0, 1) + new string('*', name.Length - 2) + name.Substring(name.Length - 1)
                : new string('*', name.Length);

            return $"{maskedName}@{domain}";
        });
    }
}
