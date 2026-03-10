using Apperture.RuntimeHost.Azure.Secrets;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        services.AddAppertureAzureKeyVaultSecrets(context.Configuration);
    })
    .Build();

host.Run();
