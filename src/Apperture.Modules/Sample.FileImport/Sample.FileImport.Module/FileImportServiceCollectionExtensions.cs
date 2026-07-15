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
        return services;
    }
}
