using Apperture.Bootstrap;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimeHost.Windows.WorkerHost;

// Windows runtime uses the shared SQL-backed operational secret store (same as Control Plane) with DPAPI protection.
// There is no separate local-only runtime secret store in the current design.

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAppertureBootstrap(builder.Configuration);
builder.Services.AddApperturePersistence(
    sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString);
builder.Services.AddAppertureSqlSecrets();

builder.Services.AddHostedService<WorkerStub>();

var host = builder.Build();
host.Run();
