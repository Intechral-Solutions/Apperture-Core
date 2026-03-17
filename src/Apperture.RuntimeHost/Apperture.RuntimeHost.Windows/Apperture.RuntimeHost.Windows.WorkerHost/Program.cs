using Apperture.Bootstrap;
using Apperture.ConnectorResolution;
using Apperture.Core.Application.Execution;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimeHost.Windows.WorkerHost;
using Apperture.RuntimePlane.Abstractions;
using Apperture.RuntimePlane.Engine;

// Windows runtime uses the shared SQL-backed operational secret store (same as Control Plane) with DPAPI protection.
// There is no separate local-only runtime secret store in the current design.

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAppertureBootstrap(builder.Configuration);
builder.Services.AddApperturePersistence(
    sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString);
builder.Services.AddAppertureSqlSecrets();
builder.Services.AddConnectorRepositories();
builder.Services.AddConnectorResolution();
builder.Services.AddPlatformSqlConnector();

// Step execution: registry with demo handler, then default workflow executor
builder.Services.AddSingleton<IStepHandlerRegistry>(_ =>
{
    var registry = new StepHandlerRegistry();
    registry.Register("Sample.HelloWorld", "DoWork", new DemoStepHandler());
    return registry;
});
builder.Services.AddScoped<IWorkflowExecutor, DefaultWorkflowExecutor>();

builder.Services.AddHostedService<StartRequestWorker>();

var host = builder.Build();
host.Run();
