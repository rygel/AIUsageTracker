using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace AIUsageTracker.Web.Tests;

public class KestrelWebApplicationFactory<TEntryPoint> : WebApplicationFactory<TEntryPoint> where TEntryPoint : class
{
    private IHost? _host;

    public string ServerAddress
    {
        get
        {
            if (_host == null)
            {
                _host = Host.CreateDefaultBuilder()
                    .ConfigureWebHostDefaults(webBuilder =>
                    {
                        webBuilder.UseKestrel(options =>
                        {
                            options.Listen(IPAddress.Loopback, 0);
                        });
                        webBuilder.UseStartup<TEntryPoint>(); // Note: This might need adjustment for top-level statements
                    })
                    .Build();
                _host.Start();
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
