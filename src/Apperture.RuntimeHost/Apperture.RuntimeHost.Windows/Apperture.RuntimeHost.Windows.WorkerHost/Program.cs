using Apperture.Bootstrap;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimeHost.Windows.WorkerHost;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAppertureBootstrap(builder.Configuration);
builder.Services.AddApperturePersistence(
    sp => sp.GetRequiredService<IBootstrapConnectionProfile>().ConnectionString);
builder.Services.AddAppertureSqlSecrets();

builder.Services.AddHostedService<WorkerStub>();

var host = builder.Build();
host.Run();
