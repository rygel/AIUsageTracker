// <copyright file="ViewTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Tests
{
    using System.Net;
    using System.Text.RegularExpressions;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    [DoNotParallelize]
    public class ViewTests : WebTestBase
    {
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

        [TestMethod]
        [DataRow("/")]
        [DataRow("/providers")]
        [DataRow("/charts")]
        [DataRow("/history")]
        [DataRow("/reliability")]
        public async Task Page_LoadsSuccessfully(string path)
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(path);
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task Dashboard_HasExpectedElements()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/");
            var html = await ReadBodyAsync(response);

            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(HasClass(html, "sidebar"), "Sidebar should be present");

            Assert.IsTrue(html.Contains("<main", StringComparison.OrdinalIgnoreCase), "Main content area should be present");
        }

        [TestMethod]
        public async Task Dashboard_ModelBinding_WithShowUsedParameter()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/?showUsed=true");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(ResponseSetCookieContains(response.Headers, "showUsedPercentage"), "showUsedPercentage cookie should be set");

            var html = await ReadBodyAsync(response);
            Assert.IsTrue(html.Contains("showUsed", StringComparison.OrdinalIgnoreCase), "UI should render with showUsed enabled.");
        }

        [TestMethod]
        public async Task Dashboard_ModelBinding_WithShowInactiveParameter()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/?showInactive=true");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(ResponseSetCookieContains(response.Headers, "showInactiveProviders"), "showInactiveProviders cookie should be set");

            var html = await ReadBodyAsync(response);
            Assert.IsTrue(html.Contains("showInactive", StringComparison.OrdinalIgnoreCase), "UI should render with showInactive enabled.");
        }

        [TestMethod]
        public async Task ProvidersPage_LoadsSuccessfully()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/providers");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task ProvidersPage_HasTableStructure()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/providers");
            var html = await ReadBodyAsync(response);

            Assert.IsTrue(html.Contains("<table", StringComparison.OrdinalIgnoreCase), "Providers table should be present");
            Assert.IsTrue(html.Contains("<th", StringComparison.OrdinalIgnoreCase), "Table should have headers");
        }

        [TestMethod]
        public async Task ChartsPage_LoadsSuccessfully()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/charts");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task ChartsPage_HasChartElements()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/charts");
            var html = await ReadBodyAsync(response);
            Assert.IsTrue(
                html.Contains("<canvas", StringComparison.OrdinalIgnoreCase),
                "Chart canvas should be present");
        }

        [TestMethod]
        public async Task HistoryPage_LoadsSuccessfully()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/history");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task HistoryPage_HasTableStructure()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/history");
            var html = await ReadBodyAsync(response);

            Assert.IsTrue(html.Contains("<table", StringComparison.OrdinalIgnoreCase), "History table should be present");
            Assert.IsTrue(html.Contains("<th", StringComparison.OrdinalIgnoreCase), "Table should have headers");
        }

        [TestMethod]
        public async Task ProviderPage_LoadsSuccessfully()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/provider/openai");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task ProviderPage_HasProviderDetails()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/provider/openai");
            var html = await ReadBodyAsync(response);
            Assert.IsTrue(html.Contains("Usage History", StringComparison.OrdinalIgnoreCase), "Usage history heading should be present");
            Assert.IsTrue(html.Contains("<table", StringComparison.OrdinalIgnoreCase), "Provider detail table should be present");
        }

        [TestMethod]
        public async Task ReliabilityPage_LoadsSuccessfully()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/reliability");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task ReliabilityPage_HasReliabilityElements()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/reliability");
            var html = await ReadBodyAsync(response);
            Assert.IsTrue(HasClass(html, "reliability-grid"), "Reliability grid should be present");
        }

        [TestMethod]
        public async Task ErrorPage_LoadsSuccessfully()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/error");
            Assert.IsNotNull(response);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        }

        [TestMethod]
        public async Task ErrorPage_HasErrorMessage()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/error?message=TestError");
            var html = await ReadBodyAsync(response);
            Assert.IsTrue(HasClass(html, "error-message"), "Error message should be present");
        }

        [TestMethod]
        public async Task Layout_HasConsistentNavigation()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/");
            var html = await ReadBodyAsync(response);

            var navMatches = Regex.Matches(
                html,
                @"href\s*=\s*string.Empty(?<href>[^string.Empty]+)string.Empty",
                RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
                RegexTimeout);
            Assert.IsTrue(navMatches.Count > 0, "Navigation should have links");

            bool hasProvidersLink = false;
            bool hasChartsLink = false;
            bool hasHistoryLink = false;

            foreach (var match in navMatches.Cast<Match>())
            {
                var href = match.Groups["href"].Value;
                if (href.Contains("/providers", StringComparison.OrdinalIgnoreCase))
                {
                    hasProvidersLink = true;
                }

                if (href.Contains("/charts", StringComparison.OrdinalIgnoreCase))
                {
                    hasChartsLink = true;
                }

                if (href.Contains("/history", StringComparison.OrdinalIgnoreCase))
                {
                    hasHistoryLink = true;
                }
            }

            Assert.IsTrue(
                hasProvidersLink || hasChartsLink || hasHistoryLink,
                "Should have navigation to main sections");
        }

        [TestMethod]
        public async Task Layout_HasThemeToggle()
        {
            using var client = CreateClient();
            using var response = await client.GetAsync("/");
            var html = await ReadBodyAsync(response);
            Assert.IsTrue(HasClass(html, "theme-toggle"), "Theme selector should expose the theme-toggle compatibility class");
        }

        private static bool HasClass(string html, string className)
        {
            return Regex.IsMatch(
                html,
                $"class\\s*=\\\"[^\\\"]*\\b{Regex.Escape(className)}\\b[^\\\"]*\\\"",
                RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture,
                RegexTimeout);
        }
    }
}
