// <copyright file="KestrelWebApplicationFactory.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Tests
{
    using System.Diagnostics;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;

    public sealed class KestrelWebApplicationFactory<TEntryPoint> : IDisposable
        where TEntryPoint : class
    {
        private readonly object _syncRoot = new();
        private readonly StringBuilder _startupOutput = new();
        private readonly string _projectPath;
        private Process? _process;
        private string? _serverAddress;
        private bool _disposed;
        private bool _initialized;

        public KestrelWebApplicationFactory()
        {
            this._projectPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AIUsageTracker.Web"));
        }

        public string ServerAddress
        {
            get
            {
                this.EnsureStarted();
                return this._serverAddress ?? throw new InvalidOperationException("Server failed to start.");
            }
        }

        public void Dispose()
        {
            if (this._disposed)
            {
                return;
            }

            this._disposed = true;

            if (this._process == null)
            {
                return;
            }

            try
            {
                if (!this._process.HasExited)
                {
                    this._process.CloseMainWindow();
                    if (!this._process.WaitForExit(5000))
                    {
                        this._process.Kill(entireProcessTree: true);
                        this._process.WaitForExit(5000);
                    }
                }
            }
            catch
            {
                // Ignore cleanup failures.
            }
            finally
            {
                this._process.Dispose();
                this._process = null;
            }
        }

        private static int GetAvailablePort()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return port;
        }

        private void EnsureStarted()
        {
            if (this._initialized)
            {
                return;
            }

            lock (this._syncRoot)
            {
                if (this._initialized)
                {
                    return;
                }

                this.StartServerProcess();
                this._initialized = true;
            }
        }

        private void StartServerProcess()
        {
            if (!Directory.Exists(this._projectPath))
            {
                throw new DirectoryNotFoundException($"Could not locate web project at '{this._projectPath}'.");
            }

            var port = GetAvailablePort();
            var address = $"http://127.0.0.1:{port}";
            var args = $"run --project \"{this._projectPath}\" --no-build --no-restore -- --urls \"{address}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = args,
                WorkingDirectory = this._projectPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            startInfo.Environment["MSBuildEnableWorkloadResolver"] = "false";
            startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
            startInfo.Environment["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

            this._process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true,
            };
            this._process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    this._startupOutput.AppendLine(args.Data);
                }
            };
            this._process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrWhiteSpace(args.Data))
                {
                    this._startupOutput.AppendLine(args.Data);
                }
            };

            if (!this._process.Start())
            {
                throw new InvalidOperationException("Failed to start dotnet process for AIUsageTracker.Web.");
            }

            this._process.BeginOutputReadLine();
            this._process.BeginErrorReadLine();

            this.WaitForServerReady(address);
            this._serverAddress = address;
        }

        private void WaitForServerReady(string address)
        {
            var port = new Uri(address).Port;
            var started = DateTime.UtcNow;
            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(30))
            {
                if (this._process == null || this._process.HasExited)
                {
                    throw new InvalidOperationException(
                        "AIUsageTracker.Web process exited before becoming available. "
                        + $"Output: {this._startupOutput}");
                }

                try
                {
                    using var ping = new TcpClient();
                    ping.Connect(IPAddress.Loopback, port);
                    return;
                }
                catch
                {
                    // Intentionally ignore startup race; keep polling.
                }

                Thread.Sleep(250);
            }

            throw new TimeoutException(
                $"AIUsageTracker.Web did not start on {address} within 30s. "
                + $"Output: {this._startupOutput}");
        }
    }
}
