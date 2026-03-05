using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace AIUsageTracker.Web.Tests;

// We inherit from Object instead of WebApplicationFactory to completely avoid the TestServer cast issues.
public class KestrelWebApplicationFactory<TEntryPoint> : IDisposable where TEntryPoint : class
{
    private IHost? _host;
    private string? _serverAddress;

    public string ServerAddress
    {
        get
        {
            if (_host == null)
            {
                InitializeHost();
            }
            return _serverAddress ?? throw new InvalidOperationException("Server address not initialized.");
        }
    }

    private void InitializeHost()
    {
        var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "AIUsageTracker.Web");
        
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseContentRoot(projectDir);
                webBuilder.UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 0); // Random port
                });
                webBuilder.UseStartup<AIUsageTracker.Web.Startup>();
            })
            .Build();

        _host.Start();

        var server = _host.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        _serverAddress = addresses?.FirstOrDefault();
        
        if (_serverAddress == null)
        {
            throw new InvalidOperationException("Could not determine server address.");
        }
    }

    public void Dispose()
    {
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        _host = null;
    }
}
