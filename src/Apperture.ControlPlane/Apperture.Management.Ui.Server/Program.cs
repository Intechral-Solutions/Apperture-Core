var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// The shell page and the WebAssembly assets only. Nothing is prerendered, so this host resolves no UI service and has no
// Management API client: every API call comes from the WebAssembly client's single pipeline (Ui.Client/Program.cs).
builder.Services.AddRazorPages();

var app = builder.Build();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(static state =>
    {
        var response = ((HttpContext)state).Response;
        response.Headers.XFrameOptions = "DENY";
        response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
        return Task.CompletedTask;
    }, context);
    await next();
});

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
var isDevelopment = app.Environment.IsDevelopment();
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

        // Development only: the Tailwind watcher (watch-ui) rewrites css/app.css in
        // place, and stylesheet URLs are unversioned, so a cached copy would survive
        // a normal refresh. Production caching is unchanged.
        if (isDevelopment && ctx.Context.Request.Path.StartsWithSegments("/css", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers["Cache-Control"] = "no-store, max-age=0";
    }
});
app.UseRouting();
app.MapRazorPages();
// The default fallback below carries ASP.NET Core's `nonfile` constraint, so a hard load (refresh, bookmark,
// shared link) of a path whose last segment contains a dot and looks like a file extension -- the native ids
// Sample.HelloWorld, Apperture.Platform.Sql and platform.schedule.expected_run_missing -- would 404 instead of
// booting the client. These detail-route families are keyed by such ids, so they get an explicit catch-all.
foreach (var prefix in new[] { "/components", "/modules", "/config/notifications/definitions" })
    app.MapFallbackToPage(prefix + "/{*path}", "/_Host");
app.MapFallbackToPage("/_Host");

app.MapDefaultEndpoints();

app.Run();
