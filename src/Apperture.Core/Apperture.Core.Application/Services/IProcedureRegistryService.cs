using Apperture.Core.Domain.Procedures;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves module-declared procedures aggregated from manifests at startup.
/// </summary>
public interface IProcedureRegistryService
{
    ValueTask<ProcedureDefinition?> GetProcedureAsync(string procedureId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ProcedureDefinition>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ProcedureDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default);
}
