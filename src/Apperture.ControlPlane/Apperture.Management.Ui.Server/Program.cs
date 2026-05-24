using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Options;
using Apperture.Management.Client;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddRazorPages();

// Register client services required for prerendering (NavRail, ControlPanel, etc.)
builder.Services.Configure<ControlPanelMenuOptions>(
    builder.Configuration.GetSection("ControlPanelMenuOptions"));
builder.Services.Configure<ShellUiOptions>(
    builder.Configuration.GetSection(ShellUiOptions.SectionName));

var uiBaseAddress = builder.Configuration["ASPNETCORE_URLS"]?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
    ?? "http://localhost:9001";
var apiBaseUrl = ManagementUiDevApiRouting.ResolveApiBaseUrl(
    builder.Configuration["ApiBaseUrl"],
    uiBaseAddress);
if (!apiBaseUrl.EndsWith("/", StringComparison.Ordinal))
    apiBaseUrl += "/";
if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var apiUri))
{
    var uiBase = Uri.TryCreate(uiBaseAddress, UriKind.Absolute, out var parsedUi)
        ? parsedUi
        : new Uri("http://localhost:9001");
    apiUri = new Uri(uiBase, apiBaseUrl);
}

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = apiUri });
builder.Services.AddScoped<IManagementClient>(sp => new ManagementClient(sp.GetRequiredService<HttpClient>()));

builder.Services.AddScoped<DomainMenuService>(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var options = sp.GetRequiredService<IOptions<ControlPanelMenuOptions>>();
    var logger = sp.GetRequiredService<ILogger<DomainMenuService>>();
    return new DomainMenuService(httpClient, options, logger);
});

builder.Services.AddScoped<IOperatorCapabilitiesService, OperatorCapabilitiesService>();

builder.Services.AddAppertureShellServices();

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
else
{
    // Prerendered WASM boot config is embedded in HTML. Cached document + fresh _framework
    // yields 404 on hashed .wasm/.pdb (stale hashes) and Blazor never starts.
    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(static ctx =>
        {
            var http = (HttpContext)ctx;
            var ct = http.Response.ContentType;
            if (!string.IsNullOrEmpty(ct) &&
                ct.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            {
                http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
                http.Response.Headers.Pragma = "no-cache";
            }
            return Task.CompletedTask;
        }, context);
        await next();
    });
}
app.UseBlazorFrameworkFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Prevent stale Blazor WASM asset caches during iterative development.
        // Hashes can change across rebuilds, and a cached boot manifest can request
        // a now-missing hashed asset => 404 => runtime fails to start.
        if (ctx.Context.Request.Path.StartsWithSegments("/_framework", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers["Cache-Control"] = "no-store, max-age=0";
    }
});
app.UseRouting();
app.MapRazorPages();
app.MapFallbackToPage("/_Host");

app.MapDefaultEndpoints();

app.Run();
