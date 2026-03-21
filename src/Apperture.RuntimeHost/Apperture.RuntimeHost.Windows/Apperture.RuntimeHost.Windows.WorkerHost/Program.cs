using Apperture.Bootstrap;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.Execution;
using Apperture.Core.Application.Services;
using Apperture.ModuleSdk.Abstractions;
using Apperture.Persistence.SqlServer;
using Apperture.Persistence.SqlServer.Options;
using Apperture.RuntimeHost.Windows.WorkerHost;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;
using Apperture.Shared.Kernel;
using Sample.HelloWorld;

// Windows runtime uses the shared SQL-backed operational secret store (same as Control Plane) with DPAPI protection.
// There is no separate local-only runtime secret store in the current design.

var builder = Host.CreateApplicationBuilder(args);

// In dev we pass bootstrap credentials via environment variables.
// Explicitly register the env-var provider so BootstrapConnectionProfile can see SqlServer:* values.
builder.Configuration.AddEnvironmentVariables();

builder.Services.Configure<WorkflowStartRequestLeaseOptions>(
    builder.Configuration.GetSection("WorkflowStartRequestLease"));
builder.Services.Configure<OperationStartRequestLeaseOptions>(
    builder.Configuration.GetSection("OperationStartRequestLease"));

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
builder.Services.AddSingleton<IModuleActivationFilter, WorkerRegistryModuleActivationFilter>();
builder.Services.AddConnectorRepositories();
builder.Services.AddConnectorResolution();
builder.Services.AddPlatformSqlConnector();

// Known modules (explicit host-controlled DI list; may include multiple modules for dev/testing and constrained composition).
// Intended deployment model remains one primary module per process (ADR-0001).
builder.Services.AddSingleton<IEnumerable<IModule>>(
    [
        new HelloWorldModule()
    ]);

builder.Services.AddSingleton<IWorkflowDefinitionResolver, WorkflowDefinitionResolver>();
builder.Services.AddSingleton<IOperationRegistryService, OperationRegistryService>();

// Step execution: registry populated by modules (no demo-only hard-coding).
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

var host = builder.Build();
host.Run();
