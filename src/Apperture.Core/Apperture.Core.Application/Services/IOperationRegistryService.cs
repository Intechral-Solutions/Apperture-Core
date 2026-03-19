using Apperture.Core.Domain.Operations;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves operation definitions (platform + module). Modules register operations at startup.
/// </summary>
public interface IOperationRegistryService
{
    /// <summary>
    /// Gets all known operation definitions.
    /// </summary>
    ValueTask<IReadOnlyList<OperationDefinition>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an operation definition by id.
    /// </summary>
    ValueTask<OperationDefinition?> GetOperationAsync(OperationId operationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all operation definitions for a module.
    /// </summary>
    ValueTask<IReadOnlyList<OperationDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an operation definition.
    /// </summary>
    ValueTask RegisterAsync(OperationDefinition definition, CancellationToken cancellationToken = default);
}
