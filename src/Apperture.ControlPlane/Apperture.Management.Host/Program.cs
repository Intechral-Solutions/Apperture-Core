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

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddControllers();
// Avatar upload is the only multipart endpoint. Cap the body well below the default 128 MB so an
// oversized request is refused by the server before any of it reaches the image decoder.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = Apperture.Core.Application.Accounts.ProfileImageLimits.MaxUploadBytes + 8192;
    options.ValueCountLimit = 32;
});
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
// The SQL structure browser is a management-plane operator tool (authorized, audited); runtime workers do not compose it.
builder.Services.AddPlatformSqlStructureBrowser();
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
app.MapManagementAuthEndpoints();
app.MapDefaultEndpoints();

app.Run();
