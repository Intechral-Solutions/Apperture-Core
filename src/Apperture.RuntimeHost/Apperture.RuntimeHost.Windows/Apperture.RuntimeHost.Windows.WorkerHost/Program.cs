using Apperture.Bootstrap;
using Apperture.ConnectorResolution;
using Apperture.ControlPlane.Security;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimeHost.Windows.WorkerHost;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using Apperture.Shared.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sample.HelloWorld;
using Unisa.CampusNexus.PaymentPlans;

// Windows runtime uses the shared SQL-backed operational secret store (same as Control Plane) with DPAPI protection.
// There is no separate local-only runtime secret store in the current design.

var startupLogger = LoggerFactory
    .Create(lb => lb.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    }))
    .CreateLogger("WorkerHostStartup");

try
{
    startupLogger.LogInformation("Worker runtime bootstrap starting.");

    var builder = Host.CreateApplicationBuilder(args);

// In dev we pass bootstrap credentials via environment variables.
// Explicitly register the env-var provider so BootstrapConnectionProfile can see SqlServer:* values.
    builder.Configuration.AddEnvironmentVariables();

    startupLogger.LogInformation(
        "Worker startup context: Environment={Environment}; RuntimeId={RuntimeId}; SqlServerConfigured={HasSqlServer}",
        builder.Environment.EnvironmentName,
        builder.Configuration["APERTURE_RUNTIME_ID"] ?? builder.Configuration["RuntimeHostIdentity:RuntimeId"] ?? "worker",
        !string.IsNullOrWhiteSpace(builder.Configuration["SqlServer:Server"]));

    builder.Services.Configure<WorkflowStartRequestLeaseOptions>(
    builder.Configuration.GetSection("WorkflowStartRequestLease"));
    builder.Services.Configure<OperationStartRequestLeaseOptions>(
    builder.Configuration.GetSection("OperationStartRequestLease"));
    builder.Services.Configure<IntegrationExecutionOptions>(
    builder.Configuration.GetSection(IntegrationExecutionOptions.SectionName));
    builder.Services.PostConfigure<IntegrationExecutionOptions>(opts =>
    {
        opts.DefaultIntegrationId ??=
            builder.Configuration["APERTURE_INTEGRATION_ID"]
            ?? builder.Configuration["Apperture:IntegrationExecution:DefaultIntegrationId"];
    });

    builder.Services.Configure<RuntimeHostIdentityOptions>(opts =>
    {
        opts.RuntimeId =
            builder.Configuration["APERTURE_RUNTIME_ID"] ??
            builder.Configuration["RuntimeHostIdentity:RuntimeId"];
    });

    builder.Services.AddAppertureBootstrap(builder.Configuration);
    builder.Services.AddApperturePersistence(
        sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString);
    builder.Services.AddAppertureSqlSecrets();
    builder.Services.AddSingleton<IEnvironmentContext, RuntimeEnvironmentContext>();
    builder.Services.AddScoped<IAuditService, SqlAuditService>();
    builder.Services.AddScoped<IModuleRegistryService, SqlModuleRegistryService>();
builder.Services.AddWorkerHostIntegrationRepository();
    builder.Services.AddSingleton<IModuleActivationFilter, WorkerRegistryModuleActivationFilter>();
    builder.Services.AddConnectorRepositories();
    builder.Services.AddConnectorResolution();
    builder.Services.AddSchedulePersistence();
    builder.Services.Configure<ScheduleDispatchOptions>(builder.Configuration.GetSection(ScheduleDispatchOptions.SectionName));
    builder.Services.PostConfigure<ScheduleDispatchOptions>(opts =>
    {
        opts.RuntimePlaneId ??= builder.Configuration["APERTURE_RUNTIME_ID"] ?? builder.Configuration["RuntimeHostIdentity:RuntimeId"] ?? "local";
    });
    builder.Services.AddSingleton<ISchedulerHostIdentity>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<ScheduleDispatchOptions>>().Value;
        var ropts = sp.GetRequiredService<IOptions<RuntimeHostIdentityOptions>>().Value;
        return new DefaultSchedulerHostIdentity(opts.SchedulerHostId ?? ropts.RuntimeId ?? "worker");
    });
    builder.Services.AddSingleton<ISchedulerClaimOptions>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<ScheduleDispatchOptions>>().Value;
        return new DefaultSchedulerClaimOptions(opts.ClaimLeaseMinutes);
    });
    builder.Services.AddRuntimeSchedulerDispatch();
    builder.Services.AddPlatformSqlConnector();
    builder.Services.AddUnisaCampusNexusPaymentPlansIntegration(registerCapabilitySlotBootstrap: false);

// Known modules (explicit host-controlled DI list; may include multiple modules for dev/testing and constrained composition).
// Intended deployment model remains one primary module per process (ADR-0001).
    builder.Services.AddSingleton<HelloWorldModule>();
    builder.Services.AddSingleton(sp => new UnisaCampusNexusPaymentPlansModule(sp.GetRequiredService<IServiceScopeFactory>()));
    builder.Services.AddSingleton<IEnumerable<IModule>>(sp => new IModule[]
    {
        sp.GetRequiredService<HelloWorldModule>(),
        sp.GetRequiredService<UnisaCampusNexusPaymentPlansModule>()
    });

    builder.Services.AddSingleton<IWorkflowDefinitionResolver, WorkflowDefinitionResolver>();
    builder.Services.AddSingleton<IOperationRegistryService, OperationRegistryService>();
    builder.Services.AddSingleton<IProcedureRegistryService, ProcedureRegistryService>();

// Step execution: registry populated by modules (no demo-only hard-coding).
// Restart-bound: handlers for all DI modules are registered here; new work for disabled modules is still blocked at definition resolution (Epic 27 — Epic27-Execution-Surface-Inventory.md).
    builder.Services.AddSingleton<IStepHandlerRegistry>(sp =>
    {
        var registry = new StepHandlerRegistry();
        var modules = sp.GetRequiredService<IEnumerable<IModule>>().ToList();
        WorkerHostStartupValidation.ValidateModules(modules);

        foreach (var module in modules)
        {
            if (module is IModuleStepHandlerRegistrar registrar)
                registrar.RegisterStepHandlers(new ModuleScopedStepHandlerRegistry(module.GetManifest().Id, registry));
        }

        WorkerHostStartupValidation.ValidateHandlerOwnership(registry, modules);
        return registry;
    });
    builder.Services.AddScoped<IWorkflowExecutor, DefaultWorkflowExecutor>();

    builder.Services.AddHostedService<StartRequestWorker>();
    builder.Services.AddHostedService<ScheduleDispatchBackgroundService>();
    builder.Services.AddHostedService<ModuleCompositionBootstrapHostedService>();

    var host = builder.Build();
    startupLogger.LogInformation("Worker runtime launch succeeded; entering run loop.");
    host.Run();
}
catch (Exception ex)
{
    startupLogger.LogCritical(ex, "Worker runtime launch failed.");
    throw;
}
