using Apperture.Management.Host.ExceptionHandling;
using Apperture.Bootstrap;
using Apperture.Connectors.CampusNexus;
using Apperture.Connectors.Sftp;
using Apperture.ConnectorResolution;
using Apperture.ControlPlane.Security;
using Apperture.RuntimePlane.Dispatch.SqlServer;
using Apperture.Management.Host;
using Apperture.Persistence.SqlServer;
using Proof.CampusNexus.StudentReview;
using Proof.CampusNexus.StudentReview.Persistence;
using Sample.HelloWorld;
using Sample.FileImport;
using Sample.ApiPolling;
using Sample.ApiPolling.Persistence;
using Unisa.CampusNexus.PaymentPlans;
using Apperture.RuntimePlane.DTFx;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
builder.Services.AddManagementHostExceptionHandling();
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
builder.Services.AddOperationItemizationPersistence();
builder.Services.AddSchedulePersistence();
builder.Services.AddAppertureSqlSecrets();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeEnrollmentService, Apperture.ControlPlane.Security.SqlRuntimeEnrollmentService>();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeIdentityService, Apperture.ControlPlane.Security.SqlRuntimeIdentityService>();
builder.Services.Configure<DevTestUserOptions>(builder.Configuration.GetSection(DevTestUserOptions.SectionName));
builder.Services.Configure<BootstrapPlatformAdminOptions>(builder.Configuration.GetSection(BootstrapPlatformAdminOptions.SectionName));
builder.Services.AddAppertureGovernance();
builder.Services.AddKnownModules(
    [new HelloWorldModule(), new FileImportModule(), new ApiPollingModule(), new UnisaCampusNexusPaymentPlansModule(), new ProofCampusNexusStudentReviewModule()]);
builder.Services.AddSampleFileImportRetryHandlers();
builder.Services.AddSampleApiPollingRetryHandlers();
builder.Services.AddAppertureRecovery();
builder.Services.AddRuntimeSchedulerDispatch();
builder.Services.AddAppertureCampusNexusConnector();
builder.Services.AddAppertureSftpConnector();
builder.Services.AddPlatformSqlConnector();
builder.Services.AddConnectorResolution();
builder.Services.AddAppertureConnectorWorkflows();
builder.Services.AddAppertureIntegrationWorkflows();
builder.Services.AddApperturePlatformEvents(builder.Configuration);
builder.Services.AddAppertureNotifications(builder.Configuration);
builder.Services.AddAttentionIncidentObservation(builder.Configuration);
builder.Services.AddApperturePlatformSettingsManagementHost(builder.Configuration);
builder.Services.AddUnisaCampusNexusPaymentPlansPersistenceAdapters(applyModulePersistenceMigrationsOnStartup: true);
builder.Services.AddUnisaCampusNexusPaymentPlansIntegration();
builder.Services.AddSampleApiPollingPersistence(
    sp => sp.GetRequiredService<Apperture.Bootstrap.IBootstrapConnectionProfile>().ConnectionString,
    applyMigrationsOnStartup: true);
builder.Services.AddProofStudentReviewPersistenceAdapter();
builder.Services.AddProofCampusNexusStudentReviewIntegration();
builder.Services.AddSampleHelloWorldIntegration();
builder.Services.AddAppertureAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseManagementHostExceptionHandling();
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

app.MapDefaultEndpoints();

app.Run();
