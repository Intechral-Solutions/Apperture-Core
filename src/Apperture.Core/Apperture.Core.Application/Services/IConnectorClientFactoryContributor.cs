namespace Apperture.Core.Application.Services;

/// <summary>
/// Connector packages implement this to register <see cref="IConnectorClientFactory{T}"/> instances
/// into <see cref="IConnectorClientFactoryRegistry"/> at host startup (after all factories are in DI).
/// </summary>
public interface IConnectorClientFactoryContributor
{
    void Register(IConnectorClientFactoryRegistry registry, IServiceProvider services);
}
