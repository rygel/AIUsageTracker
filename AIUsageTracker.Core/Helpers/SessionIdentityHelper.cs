// <copyright file="SessionIdentityHelper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text;
using System.Text.Json;

namespace AIUsageTracker.Core.Helpers;

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

    public static string? TryGetIdentityFromJwt(string? token, IEnumerable<string>? profileRootProperties)
    {
        var payload = TryDecodeJwtPayload(token);
        return payload.HasValue ? TryGetPreferredIdentity(payload.Value, profileRootProperties) : null;
    }

    public static string? TryGetPreferredIdentity(JsonElement root)
    {
        return TryGetPreferredIdentity(root, profileRootProperties: null);
    }

    public static string? TryGetPreferredIdentity(JsonElement root, IEnumerable<string>? profileRootProperties)
    {
        var directEmail = DirectEmailClaims
            .Select(claim => root.ReadString(claim))
            .FirstOrDefault(IsEmailLike);

        if (directEmail != null)
        {
            return directEmail;
        }

        foreach (var profileRootProperty in profileRootProperties ?? Array.Empty<string>())
        {
            if (!root.TryGetProperty(profileRootProperty, out var profile) ||
                profile.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var profileIdentity = ProfileIdentityClaims
                .Select(claim => profile.ReadString(claim))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

            if (profileIdentity != null)
            {
                return profileIdentity;
            }
        }

        var secondaryIdentity = SecondaryIdentityClaims
            .Select(claim => root.ReadString(claim))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        return secondaryIdentity ?? FindIdentityInJson(root);
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
                return element.EnumerateArray()
                    .Select(FindIdentityInJson)
                    .FirstOrDefault(nested => !string.IsNullOrWhiteSpace(nested));
        }

        return null;
    }

    public static bool IsEmailLike(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && value.Contains('@');
    }
}
