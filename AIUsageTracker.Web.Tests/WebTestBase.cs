// <copyright file="WebTestBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
using System.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AIUsageTracker.Web.Tests;

public abstract class WebTestBase
{
    protected static KestrelWebApplicationFactory<Program>? Factory { get; private set; }

    protected static string ServerUrl { get; private set; } = string.Empty;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void InitializeFactory(TestContext testContext)
    {
        _ = testContext;

        if (Factory == null)
        {
            Factory = new KestrelWebApplicationFactory<Program>();
            ServerUrl = Factory.ServerAddress.TrimEnd('/');
        }
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void CleanupFactory()
    {
        Factory?.Dispose();
        Factory = null;
        ServerUrl = string.Empty;
    }

    protected static HttpClient CreateClient()
    {
        return new HttpClient
        {
            BaseAddress = new Uri(ServerUrl),
        };
    }

    protected static async Task<string> ReadBodyAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    protected static bool ResponseSetCookieContains(
        HttpHeaders headers,
        string cookieName)
    {
        if (!headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return false;
        }

        foreach (var cookie in cookies)
        {
            if (cookie.Contains(cookieName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
