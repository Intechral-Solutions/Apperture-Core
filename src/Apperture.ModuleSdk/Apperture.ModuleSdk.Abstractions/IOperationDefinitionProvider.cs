using Apperture.Core.Domain.Operations;

namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Provides operation definitions contributed by a module.
/// Modules that want to expose direct executable actions implement this interface.
/// </summary>
public interface IOperationDefinitionProvider
{
    /// <summary>
    /// Enumerates operations contributed by this module.
    /// </summary>
    IAsyncEnumerable<OperationDefinition> GetOperationsAsync(
        CancellationToken cancellationToken = default);
}

