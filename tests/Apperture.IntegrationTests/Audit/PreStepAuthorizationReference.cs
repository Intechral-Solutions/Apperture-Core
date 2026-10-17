using Apperture.Core.Domain.Identity;
using Apperture.Shared.Kernel;

namespace Apperture.IntegrationTests.Audit;

/// <summary>
/// The authorization rule as it was before the <c>role.manage</c> implication was removed, applied to roles as they were stored
/// before the authorization data step ran: the Platform Administrator floor of ten combinations, the instance-level
/// <c>role.manage</c> on Role implication, and ordinary grant matching. A frozen, self-contained copy (every action, type and scope
/// is a literal) used as the "before" side of the preservation proof; it shares nothing with production code except the
/// descriptor types. The unit-test project holds the same rule as a full evaluator for the upgrade-window proof.
/// </summary>
internal static class PreStepAuthorizationReference
{
    private const string AdministratorRoleId = "role-platform-administrator";
    private const string Instance = "instance";

    /// <summary>The ten combinations the rule decided specially, as (action, resource type): rows 1 to 10 of the IAM-10 gate section 2.2.</summary>
    public static readonly IReadOnlyList<(string Action, string ResourceType)> TenCombinations =
    [
        ("user.read", "Role"),
        ("permission.assign", "Role"),
        ("role.assign", "Role"),
        ("auth.configuration.read", "InstanceConfiguration"),
        ("auth.ui_policy.manage", "InstanceConfiguration"),
        ("auth.entra.manage", "InstanceConfiguration"),
        ("user.read", "User"),
        ("user.manage", "User"),
        ("role.assign", "User"),
        ("credential.manage", "User"),
    ];

    public static bool Allows(IReadOnlyList<Role> roles, string action, string resourceType, string targetScope = Instance)
    {
        var required = new PermissionDescriptor(action, new ResourceDescriptor(resourceType, Instance));

        if (roles.Any(r => string.Equals(r.Id.Value, AdministratorRoleId, StringComparison.OrdinalIgnoreCase)) &&
            IsFloor(required, targetScope))
            return true;

        foreach (var grant in roles.SelectMany(r => r.Permissions).Distinct())
        {
            var grantScope = grant.Resource?.ScopeKey ?? Instance;
            if (!(string.Equals(targetScope, grantScope, StringComparison.Ordinal) || string.Equals(grantScope, Instance, StringComparison.Ordinal)))
                continue;

            if (Implied(required, grant, targetScope))
                return true;

            if (!string.Equals(grant.Action, required.Action, StringComparison.OrdinalIgnoreCase))
                continue;

            if (required.Resource is not null && grant.Resource is not null &&
                !string.Equals(grant.Resource.ResourceType, required.Resource.ResourceType, StringComparison.OrdinalIgnoreCase))
                continue;

            return true;
        }

        return false;
    }

    private static bool Implied(PermissionDescriptor required, PermissionDescriptor grant, string targetScope) =>
        string.Equals(grant.Action, "role.manage", StringComparison.OrdinalIgnoreCase) &&
        grant.Resource is not null &&
        string.Equals(grant.Resource.ResourceType, "Role", StringComparison.OrdinalIgnoreCase) &&
        IsFloor(required, targetScope);

    private static bool IsFloor(PermissionDescriptor required, string targetScope) =>
        string.Equals(targetScope, Instance, StringComparison.Ordinal) &&
        required.Resource is not null &&
        TenCombinations.Any(c =>
            c.Action == required.Action &&
            string.Equals(c.ResourceType, required.Resource.ResourceType, StringComparison.OrdinalIgnoreCase));
}
