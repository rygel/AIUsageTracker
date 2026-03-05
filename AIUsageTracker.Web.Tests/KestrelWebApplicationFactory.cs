using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace AIUsageTracker.Web.Tests;

public class KestrelWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
{
    private IHost? _host;

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Use the actual web project directory as the content root
        var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "AIUsageTracker.Web");
        if (Directory.Exists(projectDir))
        {
            builder.UseContentRoot(projectDir);
        }

        builder.ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0);
            });
        });

        _host = base.CreateHost(builder);
        return _host;
    }

    protected override TestServer CreateServer(IWebHostBuilder builder)
    {
        return null!;
    }

    public string ServerAddress
    {
        get
        {
            if (_host == null)
            {
                _ = CreateClient();
            }
            
            if (_host == null)
            {
                throw new InvalidOperationException("Host failed to initialize.");
            }

            var server = _host.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            return addresses?.FirstOrDefault() ?? "http://127.0.0.1:5100";
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _host?.Dispose();
        }
    }
}
