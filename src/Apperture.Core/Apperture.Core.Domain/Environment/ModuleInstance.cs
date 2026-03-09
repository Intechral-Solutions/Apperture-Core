namespace Apperture.Core.Domain.Environment;

/// <summary>
/// Configured executable occurrence of a Module Installation (e.g. one worker or one Function App). Entity.
/// </summary>
public sealed record ModuleInstance(
    ModuleInstanceId Id,
    ModuleInstallationId ModuleInstallationId,
    RuntimePlaneId RuntimePlaneId,
    string DisplayName,
    ModuleInstanceStatus Status,
    string? ConfigSnapshotRef);
