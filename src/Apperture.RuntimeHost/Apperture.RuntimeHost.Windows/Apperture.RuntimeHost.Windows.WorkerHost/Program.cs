using Apperture.RuntimeHost.Windows.WorkerHost;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<WorkerStub>();
var host = builder.Build();
host.Run();
