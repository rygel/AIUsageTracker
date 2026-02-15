using AIConsumptionTracker.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddRazorPages();
builder.Services.AddSingleton<WebDatabaseService>();

var app = builder.Build();

// Configure middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Content Security Policy - Enabled in all modes with appropriate settings
var isDevelopment = app.Environment.IsDevelopment();
app.Use(async (context, next) =>
{
    if (isDevelopment)
    {
        // Development: Allow eval and inline scripts for Browser Link/Hot Reload/HTMX
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com; " +
            "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self' ws: wss:;");
    }
    else
    {
        // Production: Strict CSP
        context.Response.Headers.Append("Content-Security-Policy",
            "default-src 'self'; " +
            "script-src 'self' https://unpkg.com; " +
            "style-src 'self' 'unsafe-inline' https://unpkg.com; " +
            "img-src 'self' data:; " +
            "font-src 'self'; " +
            "connect-src 'self';");
    }
    await next();
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

// Log startup information
var dbService = app.Services.GetRequiredService<WebDatabaseService>();
if (dbService.IsDatabaseAvailable())
{
    Console.WriteLine($"Web UI connected to database: {dbService.GetType().Name}");
}
else
{
    Console.WriteLine("WARNING: Agent database not found. Web UI will show empty data.");
    Console.WriteLine("Ensure the Agent has run at least once to initialize the database.");
}

app.Run();
