// <copyright file="MonitorPortResolver.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Sockets;

namespace AIUsageTracker.Monitor.Services;

internal static class MonitorPortResolver
{
    public static int ResolveCanonicalPort(int preferredPort, bool debug, ILogger logger)
    {
        var maxAttempts = 10;
        var attemptDelay = TimeSpan.FromMilliseconds(100);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, preferredPort);
                listener.Start();
                listener.Stop();
                if (debug)
                {
                    logger.LogDebug("Port {Port} is available on attempt {Attempt}", preferredPort, attempt);
                }

                return preferredPort;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                if (attempt < maxAttempts)
                {
                    if (debug)
                    {
                        logger.LogDebug(ex, "Port {Port} in use on attempt {Attempt}, retrying...", preferredPort, attempt);
                    }

                    Thread.Sleep(attemptDelay);
                    continue;
                }

                logger.LogWarning("Preferred port {Port} is unavailable after {Attempts} attempts.", preferredPort, maxAttempts);
                break;
            }
        }

        logger.LogWarning("Preferred port {Port} was unavailable; selecting a random high port", preferredPort);
        return GetRandomHighPort(logger);
    }

    private static int GetRandomHighPort(ILogger logger)
    {
        var random = new Random();
        const int minPort = 49152;
        const int maxPort = 65535;
        const int attempts = 200;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var candidate = random.Next(minPort, maxPort + 1);
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, candidate);
                listener.Start();
                listener.Stop();
                logger.LogInformation("Using random high port {Port}", candidate);
                return candidate;
            }
            catch (SocketException)
            {
                // Keep searching.
            }
        }

        throw new InvalidOperationException($"No available high port found in range {minPort}-{maxPort}.");
    }
}
