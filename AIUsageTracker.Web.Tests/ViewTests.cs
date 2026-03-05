using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;

namespace AIUsageTracker.Web.Tests;

[TestClass]
public class ViewTests : WebTestBase
{
    [TestMethod]
    [DataRow("/")]
    [DataRow("/providers")]
    [DataRow("/charts")]
    [DataRow("/history")]
    [DataRow("/reliability")]
    public async Task Page_LoadsSuccessfully(string path)
    {
        var response = await Page.GotoAsync($"{ServerUrl}{path}");
        Assert.IsNotNull(response);
        Assert.AreEqual(200, response.Status);
    }

    [TestMethod]
    public async Task Dashboard_HasExpectedElements()
    {
        await Page.GotoAsync(ServerUrl);
        
        // Check for common layout elements
        var sidebar = await Page.QuerySelectorAsync(".sidebar");
        Assert.IsNotNull(sidebar, "Sidebar should be present");

        var mainContent = await Page.QuerySelectorAsync("main");
        Assert.IsNotNull(mainContent, "Main content area should be present");
    }
}
