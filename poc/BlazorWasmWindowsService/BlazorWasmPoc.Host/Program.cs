using Microsoft.Extensions.Hosting;

var contentRoot = AppContext.BaseDirectory;
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRoot,
});

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "BlazorWasmPoc";
});

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

app.Run();
