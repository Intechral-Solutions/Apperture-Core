using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Options;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Apperture.Management.Ui.Client.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.Configure<ControlPanelMenuOptions>(
    builder.Configuration.GetSection("ControlPanelMenuOptions"));

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "/api";
if (!apiBaseUrl.EndsWith("/", StringComparison.Ordinal))
    apiBaseUrl += "/";

var apiUri = apiBaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
    ? new Uri(apiBaseUrl)
    : new Uri(new Uri(builder.HostEnvironment.BaseAddress), apiBaseUrl.TrimStart('/'));

builder.Services.AddScoped<DomainMenuService>(sp =>
{
    var httpClient = new HttpClient { BaseAddress = apiUri };
    var options = sp.GetRequiredService<IOptions<ControlPanelMenuOptions>>();
    var logger = sp.GetRequiredService<ILogger<DomainMenuService>>();
    return new DomainMenuService(httpClient, options, logger);
});

await builder.Build().RunAsync();
