using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Options;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();

// Register client services required for prerendering (NavRail, ControlPanel, etc.)
builder.Services.Configure<ControlPanelMenuOptions>(
    builder.Configuration.GetSection("ControlPanelMenuOptions"));

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:9000/api";
if (!apiBaseUrl.EndsWith("/", StringComparison.Ordinal))
    apiBaseUrl += "/";
var apiUri = new Uri(apiBaseUrl);

builder.Services.AddScoped<DomainMenuService>(sp =>
{
    var httpClient = new HttpClient { BaseAddress = apiUri };
    var options = sp.GetRequiredService<IOptions<ControlPanelMenuOptions>>();
    var logger = sp.GetRequiredService<ILogger<DomainMenuService>>();
    return new DomainMenuService(httpClient, options, logger);
});

var app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapFallbackToPage("/_Host");
app.Run();
