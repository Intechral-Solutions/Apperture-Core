using Apperture.RuntimeHost.Windows.SchedulerHost;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<SchedulerHostStub>();
var host = builder.Build();
host.Run();
