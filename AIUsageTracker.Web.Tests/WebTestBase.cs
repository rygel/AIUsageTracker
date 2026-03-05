using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace AIUsageTracker.Web.Tests;

public abstract class WebTestBase : PageTest
{
    protected static KestrelWebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected static string ServerUrl { get; private set; } = null!;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void InitializeFactory(TestContext _)
    {
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
    }
}
