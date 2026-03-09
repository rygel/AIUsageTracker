using AIUsageTracker.Core.Models;
using Xunit;

namespace AIUsageTracker.Tests.Core;

public class MonitorResilienceTests
{
    [Fact]
    public void MonitorInfo_SupportsErrorTracking()
    {
        var errors = new List<string>();
        var info = new MonitorInfo
        {
            Errors = errors
        };

        errors.Add("Startup status: starting");
        errors.Add("Startup status: running");

        Assert.Equal(2, info.Errors.Count);
        Assert.Contains(info.Errors, e => e.Contains("running"));
    }

    [Fact]
    public void PortBinding_AddressAlreadyInUse_HandledGracefully()
    {
        var preferredPort = 59999;

        int? boundPort = null;
        try
        {
            using var listener1 = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, preferredPort);
`n            listener1.Start();

            var thread = new System.Threading.Thread(() =>
            {
                try
                {
                    using var listener2 = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, preferredPort);
`n                    listener2.Start();
                    boundPort = preferredPort;
                }
                catch (System.Net.Sockets.SocketException)
                {
                    boundPort = null;
                }
            });
            thread.Start();
            thread.Join();
        }
        finally
        {
            // Manual cleanup
            try
            {
                using var cleanup = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, preferredPort);
                // Just testing we can bind again after failure
            }
            catch { }
        }

        Assert.Null(boundPort);
    }

    [Fact]
    public void MonitorStartupMutexTests_MutexName_Format_IsValid()
    {
        var userName = Environment.UserName;
        var mutexName = @"Global\AIUsageTracker_Monitor_" + userName;

        Assert.NotNull(mutexName);
        Assert.Contains("AIUsageTracker_Monitor_", mutexName);
        Assert.Contains(userName, mutexName);
        Assert.DoesNotContain(" ", mutexName);
    }
}
