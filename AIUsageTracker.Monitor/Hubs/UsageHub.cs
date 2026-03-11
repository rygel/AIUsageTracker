// <copyright file="UsageHub.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.SignalR;

namespace AIUsageTracker.Monitor.Hubs;

/// <summary>
/// SignalR hub for pushing real-time usage updates to connected clients.
/// </summary>
public class UsageHub : Hub
{
    /// <summary>
    /// Broadcasts a "UsageUpdated" message to all connected clients.
    /// This is typically called by the Monitor service after a successful provider refresh.
    /// </summary>
    /// <returns>A Task representing the asynchronous operation.</returns>
#pragma warning disable VSTHRD200 // SignalR hub method names are part of the external contract.
    public async Task NotifyUsageUpdated()
    {
        await this.Clients.All.SendAsync("UsageUpdated").ConfigureAwait(false);
    }

    /// <summary>
    /// Broadcasts a "RefreshStarted" message to all connected clients.
    /// </summary>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public async Task NotifyRefreshStarted()
    {
        await this.Clients.All.SendAsync("RefreshStarted").ConfigureAwait(false);
    }
#pragma warning restore VSTHRD200
}
