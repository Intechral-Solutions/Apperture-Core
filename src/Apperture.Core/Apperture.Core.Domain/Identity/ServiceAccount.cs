using Apperture.Core.Domain.Environment;

namespace Apperture.Core.Domain.Identity;

/// <summary>
/// Machine/environment identity (non-human). Entity or aggregate.
/// </summary>
public sealed record ServiceAccount(
    ServiceAccountId Id,
    string DisplayName,
    EnvironmentId EnvironmentId,
    string? CredentialRef,
    IReadOnlyList<RoleId> AssignedRoleIds,
    string? Purpose);
