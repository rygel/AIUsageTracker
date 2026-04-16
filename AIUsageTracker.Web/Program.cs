// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Web.Services;
using Serilog;

try
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var app = WebApplicationBootstrapper.Build(args, appData);
    await app.RunAsync().ConfigureAwait(false);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

public partial class Program
{
    protected Program()
    {
    }
}
