using Apperture.Shared.Kernel;

namespace Apperture.Core.Application.Services;

/// <summary>
/// What replacing a role's permission set added and removed. Recording it is attribution of the change that was made; it
/// is not a statement about whether the change should have been allowed (that is IAM-10's authorization work).
/// </summary>
public sealed record PermissionSetChange(
    IReadOnlyList<PermissionDescriptor> Added,
    IReadOnlyList<PermissionDescriptor> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;

    /// <summary>The additions and removals that turn <paramref name="before"/> into <paramref name="after"/>, in a stable order.</summary>
    public static PermissionSetChange Between(IReadOnlyList<PermissionDescriptor> before, IReadOnlyList<PermissionDescriptor> after)
    {
        var previous = new HashSet<PermissionDescriptor>(before);
        var next = new HashSet<PermissionDescriptor>(after);
        return new PermissionSetChange(
            Ordered(next.Where(p => !previous.Contains(p))),
            Ordered(previous.Where(p => !next.Contains(p))));
    }

    /// <summary>
    /// The stored identity of a permission in an audit diff: the action, then <c>@resourceType:scopeKey</c> when it names a
    /// resource. Names only, never a grant's effect.
    /// </summary>
    public static string Identity(PermissionDescriptor permission) =>
        permission.Resource is null
            ? permission.Action
            : $"{permission.Action}@{permission.Resource.ResourceType}:{permission.Resource.ScopeKey}";

    private static IReadOnlyList<PermissionDescriptor> Ordered(IEnumerable<PermissionDescriptor> permissions) =>
        permissions.OrderBy(Identity, StringComparer.Ordinal).ToList();
}
