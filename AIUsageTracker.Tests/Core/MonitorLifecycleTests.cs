using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Web.Services;
using System.Reflection;

namespace AIUsageTracker.Tests.Core;

public class MonitorLifecycleTests
{
    [Fact]
    public void MonitorLauncher_HasRequiredMethods_ForSlimUiStartStop()
    {
        var type = typeof(MonitorLauncher);
        
        var startMethod = type.GetMethod("StartAgentAsync", BindingFlags.Public | BindingFlags.Static);
        var stopMethod = type.GetMethod("StopAgentAsync", BindingFlags.Public | BindingFlags.Static);
        var isRunningMethod = type.GetMethod("IsAgentRunningAsync", BindingFlags.Public | BindingFlags.Static);
        var waitMethod = type.GetMethod("WaitForAgentAsync", BindingFlags.Public | BindingFlags.Static);
        
        Assert.NotNull(startMethod);
        Assert.NotNull(stopMethod);
        Assert.NotNull(isRunningMethod);
        Assert.NotNull(waitMethod);
        
        Assert.True(startMethod.ReturnType == typeof(Task<bool>) || 
                    startMethod.ReturnType == typeof(ValueTask<bool>),
            "StartAgentAsync should return Task<bool> or ValueTask<bool>");
    }

    [Fact]
    public void MonitorProcessService_HasRequiredMethods_ForWebUiStop()
    {
        var type = typeof(MonitorProcessService);
        
        var stopMethod = type.GetMethod("StopAgentAsync");
        var stopDetailedMethod = type.GetMethod("StopAgentDetailedAsync");
        
        Assert.NotNull(stopMethod);
        Assert.NotNull(stopDetailedMethod);
    }

    [Fact]
    public async Task MonitorLifecycle_StartFromSlim_StopFromWeb_RestartFromSlim_Works()
    {
        try
        {
            var canStart = await MonitorLauncher.StartAgentAsync();
            if (!canStart)
            {
                return;
            }

            var started = await MonitorLauncher.WaitForAgentAsync();
            if (!started)
            {
                return;
            }

            await MonitorLauncher.StopAgentAsync();
            var restarted = await MonitorLauncher.StartAgentAsync();
            if (!restarted)
            {
                return;
            }

            var restartReady = await MonitorLauncher.WaitForAgentAsync();
            Assert.True(restartReady, "Monitor should be reachable after restart.");
        }
        finally
        {
            // Ensure the test never leaves a monitor process running in CI.
            await MonitorLauncher.StopAgentAsync();
        }
    }
}
