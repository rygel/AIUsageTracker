// <copyright file="MainWindow.SignalR.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.UI.Slim;

public partial class MainWindow : Window
{
    private async Task InitializeSignalRAsync()
    {
        try
        {
            var hubUrl = $"{this._monitorService.AgentUrl.TrimEnd('/')}/hubs/usage";
            this._logger.LogInformation("Initializing SignalR connection to {HubUrl}", hubUrl);

            this._hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            this._hubConnection.On("RefreshStarted", async () =>
            {
                await this.Dispatcher.InvokeAsync(() =>
                {
                    this.ShowStatus("Monitor refreshing...", StatusType.Info);
                }).Task.ConfigureAwait(true);
            });

            this._hubConnection.On("UsageUpdated", async () =>
            {
                this._logger.LogInformation("SignalR: Received UsageUpdated event");
                await this.Dispatcher.InvokeAsync(async () =>
                {
                    await this.FetchDataAsync(" (real-time)").ConfigureAwait(true);
                }).Task.ConfigureAwait(true);
            });

            await this._hubConnection.StartAsync().ConfigureAwait(true);
            this._logger.LogInformation("SignalR connection established");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            this._logger.LogWarning(ex, "Failed to initialize SignalR connection. Falling back to polling only.");
        }
    }
}
