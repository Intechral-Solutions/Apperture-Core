using Apperture.Core.Domain.Identity;

namespace Apperture.Core.Application.Services;

/// <summary>
/// Result of attempting to create the first platform administrator. First-admin bootstrap only; succeeds when zero users exist.
/// </summary>
public sealed record FirstAdminBootstrapResult
{
    public FirstAdminBootstrapResultKind Kind { get; }
    public UserId? UserId { get; }

    private FirstAdminBootstrapResult(FirstAdminBootstrapResultKind kind, UserId? userId = null)
    {
        Kind = kind;
        UserId = userId;
    }

    public static FirstAdminBootstrapResult Success(UserId userId) =>
        new(FirstAdminBootstrapResultKind.Success, userId);

    public static FirstAdminBootstrapResult AlreadyExists =>
        new(FirstAdminBootstrapResultKind.AlreadyExists);

    public static FirstAdminBootstrapResult RoleNotFound =>
        new(FirstAdminBootstrapResultKind.RoleNotFound);

    public static FirstAdminBootstrapResult DuplicateIdentityLink =>
        new(FirstAdminBootstrapResultKind.DuplicateIdentityLink);
}

/// <summary>
/// Kind of first-admin bootstrap outcome.
/// </summary>
public enum FirstAdminBootstrapResultKind
{
    Success,
    AlreadyExists,
    RoleNotFound,
    DuplicateIdentityLink
}
