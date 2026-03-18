using Apperture.Bootstrap;
using Apperture.ControlPlane.Security;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using Sample.HelloWorld;
using Apperture.RuntimePlane.DTFx;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddCors(options =>
{
    options.AddPolicy("UiDev", policy =>
        policy
            .WithOrigins("http://localhost:9001", "https://localhost:9001")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
builder.Services.AddAppertureBootstrap(builder.Configuration);
builder.Services.AddAppertureDTFx();
builder.Services.AddApperturePersistence(
    sp => sp.GetRequiredService<Apperture.Bootstrap.IBootstrapConnectionProfile>().ConnectionString);
builder.Services.AddApperturePersistenceMigrationsOnStartup();
builder.Services.AddAppertureSqlSecrets();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeEnrollmentService, Apperture.ControlPlane.Security.SqlRuntimeEnrollmentService>();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeIdentityService, Apperture.ControlPlane.Security.SqlRuntimeIdentityService>();
builder.Services.AddAppertureGovernance();
builder.Services.AddKnownModules([new HelloWorldModule()]);
builder.Services.AddAppertureRecovery();
builder.Services.AddAppertureConnectorWorkflows();
builder.Services.AddAppertureAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseCors("UiDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Entra: redirect to IdP. Local-only: return 401 (client uses POST /api/auth/local/login).
app.MapGet("/signin", async (HttpContext ctx, IOptions<EntraAuthOptions> entra) =>
{
    if (entra.Value.Enabled)
        await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    else
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
});

app.MapGet("/signout", (HttpContext ctx) => ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme));

app.Run();
