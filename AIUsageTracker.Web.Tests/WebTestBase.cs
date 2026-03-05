using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace AIUsageTracker.Web.Tests;

public abstract class WebTestBase : PageTest
{
    protected static KestrelWebApplicationFactory<AIUsageTracker.Web.Startup>? Factory { get; private set; }
    protected static string ServerUrl { get; private set; } = null!;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void InitializeFactory(TestContext _)
    {
        if (Factory == null)
        {
            Factory = new KestrelWebApplicationFactory<AIUsageTracker.Web.Startup>();
            ServerUrl = Factory.ServerAddress.TrimEnd('/');
        }
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void CleanupFactory()
    {
        Factory?.Dispose();
        Factory = null;
    }
}
