// <copyright file="SessionIdentityHelperTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.Helpers
{
    using System.Text;
    using System.Text.Json;
    using AIUsageTracker.Core.Helpers;

    public class SessionIdentityHelperTests
    {
        [Fact]
        public void TryDecodeJwtPayload_WithValidJwt_ReturnsPayload()
        {
            var token = CreateJwt(new
            {
                email = "user@example.com",
                plan_type = "plus"
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
            using var doc = JsonDocument.Parse(string.Empty"
            {
              "email": "user@example.com",
              "login": "fallback-user"
            }
            string.Empty");

            var identity = SessionIdentityHelper.TryGetPreferredIdentity(doc.RootElement);

            Assert.Equal("user@example.com", identity);
        }

        [Fact]
        public void TryGetPreferredIdentity_FallsBackToOpenAiProfileIdentity()
        {
            using var doc = JsonDocument.Parse(string.Empty"
            {
              "https://api.openai.com/profile": {
                "username": "profile-user"
              }
            }
            string.Empty");

            var identity = SessionIdentityHelper.TryGetPreferredIdentity(doc.RootElement);

            Assert.Equal("profile-user", identity);
        }

        [Fact]
        public void TryGetPreferredIdentity_FallsBackToRecursiveIdentitySearch()
        {
            using var doc = JsonDocument.Parse(string.Empty"
            {
              "outer": {
                "innerUser": "nested-user"
              }
            }
            string.Empty");

            var identity = SessionIdentityHelper.TryGetPreferredIdentity(doc.RootElement);

            Assert.Equal("nested-user", identity);
        }

        [Fact]
        public void TryGetIdentityFromJwt_ReadsPreferredUsername()
        {
            var token = CreateJwt(new
            {
                preferred_username = "codex@example.com"
            });

            var identity = SessionIdentityHelper.TryGetIdentityFromJwt(token);

            Assert.Equal("codex@example.com", identity);
        }

        [Theory]
        [InlineData("user@example.com", true)]
        [InlineData("username", false)]
        [InlineData(string.Empty, false)]
        [InlineData(null, false)]
        public void IsEmailLike_ReturnsExpectedValue(string? value, bool expected)
        {
            Assert.Equal(expected, SessionIdentityHelper.IsEmailLike(value));
        }
    `n
        private static string CreateJwt(object payload)
        {
            var header = Base64UrlEncode(string.Empty"{"alg":"none","typ":"JWT"}string.Empty");
            var body = Base64UrlEncode(JsonSerializer.Serialize(payload));
            return $"{header}.{body}.";
        }
    `n
        private static string Base64UrlEncode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
