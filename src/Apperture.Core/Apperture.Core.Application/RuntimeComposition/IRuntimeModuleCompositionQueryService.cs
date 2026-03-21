namespace Apperture.Core.Application.RuntimeComposition;

/// <summary>
/// Projects host-effective module execution composition for operators and runtime diagnostics (Epic 27).
/// </summary>
public interface IRuntimeModuleCompositionQueryService
{
    ValueTask<HostEffectiveModuleSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}
