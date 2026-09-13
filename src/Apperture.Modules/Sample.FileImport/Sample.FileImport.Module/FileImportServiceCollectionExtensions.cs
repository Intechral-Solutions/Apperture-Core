using Microsoft.Extensions.DependencyInjection;
using Sample.FileImport.Contracts;

namespace Sample.FileImport;

public static class FileImportServiceCollectionExtensions
{
    /// <summary>
    /// Registers process-local <see cref="IImportOutcomeStore"/> and <see cref="FileImportModule"/>.
    /// </summary>
    public static IServiceCollection AddSampleFileImport(this IServiceCollection services)
    {
        services.AddSingleton<IImportOutcomeStore, InMemoryImportOutcomeStore>();
        services.AddSingleton(sp => new FileImportModule(sp.GetRequiredService<IImportOutcomeStore>()));
        services.AddSampleFileImportRetryHandlers();
        return services;
    }

    /// <summary>
    /// Registers FileImport <see cref="IOperationItemRetryHandler"/> for Management Host (SDK-4A).
    /// Safe to call when the module instance is registered separately via AddKnownModules.
    /// </summary>
    public static IServiceCollection AddSampleFileImportRetryHandlers(this IServiceCollection services)
    {
        services.AddSingleton<Apperture.Core.Application.OperationItems.IOperationItemRetryHandler, FileImportOperationItemRetryHandler>();
        return services;
    }
}
