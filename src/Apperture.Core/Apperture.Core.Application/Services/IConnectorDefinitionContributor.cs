using Apperture.Core.Domain.Connector;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Contributes or enriches <see cref="ConnectorDefinition"/> entries in <see cref="IConnectorDefinitionRegistry"/> at host startup.
/// Use for module- or connector-package-provided metadata (field descriptors, descriptions). Does not expose executable operations.
/// </summary>
public interface IConnectorDefinitionContributor
{
    /// <summary>
    /// Registers definitions; later calls with the same id overwrite earlier registrations for that id.
    /// </summary>
    void Contribute(IConnectorDefinitionRegistry registry);
}
