using Apperture.Core.Domain.Procedures;
using Apperture.Shared.Kernel;

namespace Apperture.ModuleSdk.Abstractions;

/// <summary>
/// Declares a named operator procedure contributed by a module (manifest-time).
/// </summary>
public sealed record ModuleProcedureDeclaration(
    string Id,
    string DisplayName,
    string? Description,
    string? Category,
    ProcedureLaunchKind LaunchKind,
    string TargetDefinitionId,
    string? ParameterSchema,
    IReadOnlyList<PermissionDescriptor>? RequiredPermissions = null);
