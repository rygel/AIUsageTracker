using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace AIUsageTracker.Web.Tests;

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
        var projectDir = ResolveProjectDirectory();
        
        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseContentRoot(projectDir);
                webBuilder.UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 0); // Random port
                });
                webBuilder.UseStartup<TEntryPoint>();
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

    private static string ResolveProjectDirectory()
    {
        // Try local development path (relative to bin/Debug/net8.0)
        var localPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Web");
        if (Directory.Exists(localPath))
        {
            return Path.GetFullPath(localPath);
        }

        // Try CI path (relative to repo root if current directory is root)
        var ciPath = Path.Combine(Directory.GetCurrentDirectory(), "AIUsageTracker.Web");
        if (Directory.Exists(ciPath))
        {
            return Path.GetFullPath(ciPath);
        }

        // Fallback: search up for solution root
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "AIUsageTracker.Web");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find AIUsageTracker.Web project directory.");
    }

    public void Dispose()
    {
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        _host = null;
    }
}
