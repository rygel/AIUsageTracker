// <copyright file="SessionIdentityHelperTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Helpers;

namespace AIUsageTracker.Tests.Helpers;

public class SessionIdentityHelperTests
{
    [Fact]
    public void TryDecodeJwtPayload_WithValidJwt_ReturnsPayload()
    {
        var token = CreateJwt(new
        {
            email = "user@example.com",
            plan_type = "plus",
        });

        var payload = SessionIdentityHelper.TryDecodeJwtPayload(token);

        Assert.True(payload.HasValue);
        Assert.Equal("user@example.com", payload.Value.ReadString("email"));
        Assert.Equal("plus", payload.Value.ReadString("plan_type"));
    }

    [Fact]
    public void TryDecodeJwtPayload_WithInvalidJwt_ReturnsNull()
    {
        var payload = SessionIdentityHelper.TryDecodeJwtPayload("not-a-jwt");

        Assert.False(payload.HasValue);
    }

    [Fact]
    public void TryGetPreferredIdentity_PrefersDirectEmailClaim()
    {
        using var doc = JsonDocument.Parse("""
        {
          "email": "user@example.com",
          "login": "fallback-user"
        }
        """);

        var identity = SessionIdentityHelper.TryGetPreferredIdentity(doc.RootElement);

        Assert.Equal("user@example.com", identity);
    }

    [Fact]
    public void TryGetPreferredIdentity_FallsBackToOpenAiProfileIdentity()
    {
        using var doc = JsonDocument.Parse("""
        {
          "https://api.openai.com/profile": {
            "username": "profile-user"
          }
        }
        """);

        var identity = SessionIdentityHelper.TryGetPreferredIdentity(doc.RootElement);

        Assert.Equal("profile-user", identity);
    }

    [Fact]
    public void TryGetPreferredIdentity_FallsBackToRecursiveIdentitySearch()
    {
        using var doc = JsonDocument.Parse("""
        {
          "outer": {
            "innerUser": "nested-user"
          }
        }
        """);

        var identity = SessionIdentityHelper.TryGetPreferredIdentity(doc.RootElement);

        Assert.Equal("nested-user", identity);
    }

    [Fact]
    public void TryGetIdentityFromJwt_ReadsPreferredUsername()
    {
        var token = CreateJwt(new
        {
            preferred_username = "codex@example.com",
        });

        var identity = SessionIdentityHelper.TryGetIdentityFromJwt(token);

        Assert.Equal("codex@example.com", identity);
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("username", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsEmailLike_ReturnsExpectedValue(string? value, bool expected)
    {
        Assert.Equal(expected, SessionIdentityHelper.IsEmailLike(value));
    }

    private static string CreateJwt(object payload)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var body = Base64UrlEncode(JsonSerializer.Serialize(payload));
        return $"{header}.{body}.";
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
