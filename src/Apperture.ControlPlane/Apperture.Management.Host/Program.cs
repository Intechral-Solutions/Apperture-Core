using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.DTFx;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAppertureBootstrap(builder.Configuration);
builder.Services.AddAppertureDTFx();
builder.Services.AddApperturePersistence(
    sp => sp.GetRequiredService<Apperture.Bootstrap.IBootstrapConnectionProfile>().ConnectionString);
builder.Services.AddAppertureSqlSecrets();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeEnrollmentService, Apperture.ControlPlane.Security.SqlRuntimeEnrollmentService>();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeIdentityService, Apperture.ControlPlane.Security.SqlRuntimeIdentityService>();
builder.Services.AddAppertureGovernance();
builder.Services.AddAppertureConnectorWorkflows();

// Auth scaffolding (Epic 1): placeholder scheme so pipeline runs; replace with Entra/local in later epics.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = PlaceholderAuthHandler.SchemeName;
    options.DefaultScheme = PlaceholderAuthHandler.SchemeName;
})
.AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, PlaceholderAuthHandler>(
    PlaceholderAuthHandler.SchemeName,
    "Placeholder; replace with real auth in Epic 2.",
    _ => { });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
