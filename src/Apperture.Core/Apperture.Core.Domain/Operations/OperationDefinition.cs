using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Operations;

/// <summary>
/// Platform or module-defined executable operation (procedure/workflow). Entity.
/// </summary>
public sealed record OperationDefinition(
    OperationId Id,
    string Name,
    string? ModuleId,
    OperationCategory Category,
    string? ParameterSchema,
    IReadOnlyList<PermissionDescriptor> RequiredPermissions);
