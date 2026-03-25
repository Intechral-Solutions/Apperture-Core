using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Procedures;

/// <summary>
/// Module-declared operator procedure (stable id, launch target, parameter schema).
/// </summary>
public sealed record ProcedureDefinition(
    string Id,
    string ModuleId,
    string DisplayName,
    string? Description,
    string? Category,
    ProcedureLaunchKind LaunchKind,
    string TargetDefinitionId,
    string? ParameterSchema,
    IReadOnlyList<PermissionDescriptor>? RequiredPermissions);
