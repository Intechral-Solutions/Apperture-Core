using Apperture.Bootstrap;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.ConnectorResolution;
using Apperture.ControlPlane.Security;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using Proof.CampusNexus.StudentReview;
using Sample.HelloWorld;
using Unisa.CampusNexus.PaymentPlans;
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
builder.Services.AddSchedulePersistence();
builder.Services.AddAppertureSqlSecrets();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeEnrollmentService, Apperture.ControlPlane.Security.SqlRuntimeEnrollmentService>();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeIdentityService, Apperture.ControlPlane.Security.SqlRuntimeIdentityService>();
builder.Services.Configure<DevTestUserOptions>(builder.Configuration.GetSection(DevTestUserOptions.SectionName));
builder.Services.Configure<BootstrapPlatformAdminOptions>(builder.Configuration.GetSection(BootstrapPlatformAdminOptions.SectionName));
builder.Services.AddAppertureGovernance();
builder.Services.AddKnownModules(
    [new HelloWorldModule(), new UnisaCampusNexusPaymentPlansModule(), new ProofCampusNexusStudentReviewModule()]);
builder.Services.AddAppertureRecovery();
builder.Services.AddRuntimeSchedulerDispatch();
builder.Services.AddAppertureCampusNexusConnector();
builder.Services.AddAppertureSftpConnector();
builder.Services.AddPlatformSqlConnector();
builder.Services.AddAppertureConnectorWorkflows();
builder.Services.AddAppertureIntegrationWorkflows();
builder.Services.AddAttentionIncidentObservation(builder.Configuration);
builder.Services.AddUnisaCampusNexusPaymentPlansIntegration(applyModulePersistenceMigrationsOnStartup: true);
builder.Services.AddProofCampusNexusStudentReviewIntegration();
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
    if (EntraRuntimeState.IsUsable(entra.Value))
        await ctx.ChallengeAsync(OpenIdConnectDefaults.AuthenticationScheme);
    else
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
});

app.MapGet("/signout", (HttpContext ctx) => ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme));

app.Run();
