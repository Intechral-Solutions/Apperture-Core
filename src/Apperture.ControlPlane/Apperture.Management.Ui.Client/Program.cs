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

var apiBaseUrl = ManagementUiDevApiRouting.ResolveApiBaseUrl(
    builder.Configuration["ApiBaseUrl"],
    builder.HostEnvironment.BaseAddress);

if (!apiBaseUrl.EndsWith("/", StringComparison.Ordinal))
    apiBaseUrl += "/";

var apiUri = apiBaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
    ? new Uri(apiBaseUrl)
    : new Uri(new Uri(builder.HostEnvironment.BaseAddress), apiBaseUrl.TrimStart('/'));

builder.Services.AddScoped<IncludeCredentialsHandler>();
builder.Services.AddScoped<BrowserRequestHeadersHandler>();
builder.Services.AddSessionRecovery(apiUri);

// The single HttpClient pipeline every Management UI request uses. Order matters:
// IncludeCredentialsHandler -> BrowserRequestHeadersHandler -> AuthRecoveryHandler -> HttpClientHandler.
builder.Services.AddScoped(sp =>
{
    var credentials = sp.GetRequiredService<IncludeCredentialsHandler>();
    var headers = sp.GetRequiredService<BrowserRequestHeadersHandler>();
    var recovery = sp.GetRequiredService<AuthRecoveryHandler>();
    credentials.InnerHandler = headers;
    headers.InnerHandler = recovery;
    recovery.InnerHandler = new HttpClientHandler();
    return new HttpClient(credentials) { BaseAddress = apiUri };
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
