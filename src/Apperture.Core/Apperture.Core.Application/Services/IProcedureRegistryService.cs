using Apperture.Core.Domain.Procedures;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Resolves module-declared procedures from manifests, reflecting the current module activation filter (rebuilt when enabled modules change).
/// </summary>
public interface IProcedureRegistryService
{
    ValueTask<ProcedureDefinition?> GetProcedureAsync(string procedureId, CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ProcedureDefinition>> ListAsync(CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ProcedureDefinition>> GetByModuleAsync(string moduleId, CancellationToken cancellationToken = default);

    ValueTask<ProcedureDefinition?> FindByTargetAsync(
        string moduleId,
        ProcedureLaunchKind launchKind,
        string targetDefinitionId,
        CancellationToken cancellationToken = default);
}
