// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Web.Services;
using Serilog;

try
{
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    var app = WebApplicationBootstrapper.Build(args, appData);
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program
{
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
    }
}
