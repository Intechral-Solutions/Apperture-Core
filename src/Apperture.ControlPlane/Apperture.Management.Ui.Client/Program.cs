using Apperture.Management.Ui.Client.Services;
using Apperture.Management.Ui.Shared.Options;
using Apperture.Management.Client;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Options;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Apperture.Management.Ui.Client.App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.Configure<ControlPanelMenuOptions>(
    builder.Configuration.GetSection("ControlPanelMenuOptions"));
builder.Services.Configure<ShellUiOptions>(
    builder.Configuration.GetSection(ShellUiOptions.SectionName));

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "/api";
// Relative /api is resolved against the WASM origin. Local dev serves UI on :9001 and API on :9000; if Blazor
// environment is not Development, appsettings.Development.json is not merged and we would call the wrong host.
if (!apiBaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
    && Uri.TryCreate(builder.HostEnvironment.BaseAddress, UriKind.Absolute, out var wasmBase)
    && wasmBase.IsLoopback
    && wasmBase.Port == 9001)
{
    apiBaseUrl = "http://localhost:9000/api";
}

if (!apiBaseUrl.EndsWith("/", StringComparison.Ordinal))
    apiBaseUrl += "/";

var apiUri = apiBaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
    ? new Uri(apiBaseUrl)
    : new Uri(new Uri(builder.HostEnvironment.BaseAddress), apiBaseUrl.TrimStart('/'));

builder.Services.AddScoped<IncludeCredentialsHandler>();

builder.Services.AddScoped(sp =>
{
    var handler = sp.GetRequiredService<IncludeCredentialsHandler>();
    handler.InnerHandler = new HttpClientHandler();
    return new HttpClient(handler) { BaseAddress = apiUri };
});

builder.Services.AddScoped<DomainMenuService>(sp =>
{
    var httpClient = sp.GetRequiredService<HttpClient>();
    var options = sp.GetRequiredService<IOptions<ControlPanelMenuOptions>>();
    var logger = sp.GetRequiredService<ILogger<DomainMenuService>>();
    return new DomainMenuService(httpClient, options, logger);
});

builder.Services.AddScoped<IManagementClient>(sp =>
    new ManagementClient(sp.GetRequiredService<HttpClient>()));

builder.Services.AddScoped<IOperatorCapabilitiesService, OperatorCapabilitiesService>();

builder.Services.AddAppertureShellServices();

await builder.Build().RunAsync();
