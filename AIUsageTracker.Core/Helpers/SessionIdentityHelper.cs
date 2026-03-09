// <copyright file="SessionIdentityHelper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Helpers
{
    using System.Text;
    using System.Text.Json;

    public static class SessionIdentityHelper
    {
        private static readonly string[] DirectEmailClaims = { "email", "upn", "preferred_username" };
        private static readonly string[] ProfileIdentityClaims = { "email", "username", "name" };
        private static readonly string[] SecondaryIdentityClaims = { "username", "login", "name", "sub" };

        public static JsonElement? TryDecodeJwtPayload(string? token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            try
            {
                var payload = parts[1].Replace('-', '+').Replace('_', '/');
                switch (payload.Length % 4)
                {
                    case 2:
                        payload += "==";
                        break;
                    case 3:
                        payload += "=";
                        break;
                }

                var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            catch
            {
                return null;
            }
        }

        public static string? TryGetIdentityFromJwt(string? token)
        {
            var payload = TryDecodeJwtPayload(token);
            return payload.HasValue ? TryGetPreferredIdentity(payload.Value) : null;
        }

        public static string? TryGetPreferredIdentity(JsonElement root)
        {
            foreach (var claim in DirectEmailClaims)
            {
                var value = root.ReadString(claim);
                if (IsEmailLike(value))
                {
                    return value;
                }
            }

            if (root.TryGetProperty("https://api.openai.com/profile", out var profile) &&
                profile.ValueKind == JsonValueKind.Object)
            {
                foreach (var claim in ProfileIdentityClaims)
                {
                    var value = profile.ReadString(claim);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }
            }

            foreach (var claim in SecondaryIdentityClaims)
            {
                var value = root.ReadString(claim);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return FindIdentityInJson(root);
        }

        public static string? FindIdentityInJson(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.String)
                        {
                            var value = property.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                var key = property.Name.ToLowerInvariant();
                                if (key.Contains("email", StringComparison.Ordinal) ||
                                    key.Contains("username", StringComparison.Ordinal) ||
                                    key.Contains("login", StringComparison.Ordinal) ||
                                    key.Contains("user", StringComparison.Ordinal))
                                {
                                    return value;
                                }
                            }
                        }

                        var nested = FindIdentityInJson(property.Value);
                        if (!string.IsNullOrWhiteSpace(nested))
                        {
                            return nested;
                        }
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        var nested = FindIdentityInJson(item);
                        if (!string.IsNullOrWhiteSpace(nested))
                        {
                            return nested;
                        }
                    }

                    break;
            }

            return null;
        }

        public static bool IsEmailLike(string? value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.Contains('@');
        }
    }
}
