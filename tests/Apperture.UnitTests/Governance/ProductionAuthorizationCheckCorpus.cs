using System.Text.RegularExpressions;
using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.UnitTests.Governance;

/// <summary>
/// One place in production source that asks the authorization evaluator for one of the permissions the Platform
/// Administrator floor and the <c>role.manage</c> implication are about. <see cref="Action"/> and
/// <see cref="ResourceType"/> are the names of the <see cref="ActionConstants"/> and <see cref="ResourceTypeConstants"/>
/// members exactly as the source spells them. <see cref="ChecksPerFile"/> is how many such checks the file holds.
/// </summary>
internal sealed record ProductionAuthorizationCheck(string File, string Action, string ResourceType, int ChecksPerFile = 1)
{
    public PermissionDescriptor ToPermission() =>
        new(ConstantValue(typeof(ActionConstants), Action), new ResourceDescriptor(ConstantValue(typeof(ResourceTypeConstants), ResourceType), ScopeKeyConstants.Instance));

    private static string ConstantValue(Type holder, string name) =>
        (string)(holder.GetField(name)?.GetRawConstantValue() ?? throw new InvalidOperationException($"{holder.Name}.{name} does not exist; update the corpus."));
}

/// <summary>
/// A source file that mentions one of these actions without asking for it: a definition, a seed, a repair list or the
/// rule itself. Such a file is counted by action, because the resource type is not what it decides.
/// </summary>
internal sealed record AuthorizationDefinitionSite(string File, string Action, int References, string Role);

/// <summary>
/// The corpus of production authorization checks for the permissions that the evaluator treats specially (the ten
/// combinations of the Platform Administrator floor, plus <c>role.manage</c>, which implies them), derived by reading
/// the source and guarded against going stale by <c>ProductionAuthorizationCheckCorpusGuardTests</c>.
/// </summary>
/// <remarks>
/// Every check passes scope <c>instance</c>: the dashboard and capability helpers build
/// <c>ScopeKeyConstants.Instance</c> themselves, and every direct call spells it. The differential tests ask each of these
/// of every generated role shape. File paths are relative to <c>src/</c>.
/// </remarks>
internal static class ProductionAuthorizationCheckCorpus
{
    private const string Security = "Apperture.ControlPlane/Apperture.ControlPlane.Security/";
    private const string Controllers = "Apperture.ControlPlane/Apperture.Management.Api/Controllers/";

    private const string UserRead = nameof(ActionConstants.UserRead);
    private const string UserManage = nameof(ActionConstants.UserManage);
    private const string RoleAssign = nameof(ActionConstants.RoleAssign);
    private const string RoleManage = nameof(ActionConstants.RoleManage);
    private const string PermissionAssign = nameof(ActionConstants.PermissionAssign);
    private const string CredentialManage = nameof(ActionConstants.CredentialManage);
    private const string AuthConfigurationRead = nameof(ActionConstants.AuthConfigurationRead);
    private const string AuthUiPolicyManage = nameof(ActionConstants.AuthUiPolicyManage);
    private const string AuthEntraManage = nameof(ActionConstants.AuthEntraManage);

    private const string User = nameof(ResourceTypeConstants.User);
    private const string Role = nameof(ResourceTypeConstants.Role);
    private const string InstanceConfiguration = nameof(ResourceTypeConstants.InstanceConfiguration);

    /// <summary>Every production check, one entry per (file, action, resource type) with a count when a file asks more than once.</summary>
    public static readonly IReadOnlyList<ProductionAuthorizationCheck> Checks =
    [
        new(Security + "Dashboard/AuthWarningsDashboardCardProvider.cs", AuthConfigurationRead, InstanceConfiguration),
        new(Security + "Dashboard/AuthWarningsDashboardCardProvider.cs", AuthUiPolicyManage, InstanceConfiguration),
        new(Security + "Dashboard/AuthWarningsDashboardCardProvider.cs", AuthEntraManage, InstanceConfiguration),
        new(Security + "Dashboard/GovernanceDashboardCardProvider.cs", UserRead, Role),
        new(Security + "EntraAccessRequestService.cs", UserManage, User),
        new(Security + "EntraProvisioningDirectoryService.cs", UserManage, User),
        new(Security + "ManagedContactManagementService.cs", UserRead, User),
        new(Security + "OperatorCapabilitiesQueryService.cs", UserRead, Role),
        new(Security + "OperatorCapabilitiesQueryService.cs", PermissionAssign, Role),
        new(Security + "OperatorCapabilitiesQueryService.cs", RoleAssign, Role),
        new(Security + "OperatorCapabilitiesQueryService.cs", AuthUiPolicyManage, InstanceConfiguration),
        new(Security + "OperatorCapabilitiesQueryService.cs", AuthConfigurationRead, InstanceConfiguration),
        new(Security + "OperatorCapabilitiesQueryService.cs", AuthEntraManage, InstanceConfiguration),
        new(Security + "OperatorCapabilitiesQueryService.cs", UserManage, User),
        new(Security + "OperatorCapabilitiesQueryService.cs", CredentialManage, User),
        new(Security + "OperatorCapabilitiesQueryService.cs", RoleManage, Role),
        new(Security + "UserManagementService.cs", UserRead, User),
        new(Security + "UserManagementService.cs", UserManage, User),
        new(Security + "UserManagementService.cs", RoleAssign, Role),
        new(Security + "UserManagementService.cs", CredentialManage, User),
        new(Security + "UserManagementService.cs", RoleManage, Role),
        new(Security + "UserManagementService.cs", PermissionAssign, Role),
        new(Security + "UserProvisioningService.cs", RoleAssign, Role),
        new(Security + "UserProvisioningService.cs", UserManage, User),
        new(Controllers + "ConfigAuthOverviewController.cs", AuthConfigurationRead, InstanceConfiguration),
        new(Controllers + "ConfigAuthOverviewController.cs", AuthUiPolicyManage, InstanceConfiguration),
        new(Controllers + "ConfigAuthUiPolicyController.cs", AuthConfigurationRead, InstanceConfiguration),
        new(Controllers + "ConfigAuthUiPolicyController.cs", AuthUiPolicyManage, InstanceConfiguration),
        new(Controllers + "ConfigEntraTenantsController.cs", AuthConfigurationRead, InstanceConfiguration),
        new(Controllers + "ConfigEntraTenantsController.cs", AuthEntraManage, InstanceConfiguration, ChecksPerFile: 2),
        new(Controllers + "GovernanceController.cs", UserRead, Role),
        new(Controllers + "GovernanceController.cs", UserRead, User),
    ];

    /// <summary>Files that name these actions without asking for them, with how many times and why.</summary>
    public static readonly IReadOnlyList<AuthorizationDefinitionSite> DefinitionSites =
    [
        // The catalog: each permission is defined once.
        new(Security + "PlatformPermissionSeeder.cs", AuthUiPolicyManage, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", AuthConfigurationRead, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", AuthEntraManage, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", RoleManage, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", PermissionAssign, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", UserRead, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", UserManage, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", RoleAssign, 1, "catalog definition"),
        new(Security + "PlatformPermissionSeeder.cs", CredentialManage, 1, "catalog definition"),

        // The grants the startup repair forces onto the Platform Administrator role.
        new(Security + "PlatformAdminRolePermissionRepair.cs", UserRead, 2, "administrator repair list"),
        new(Security + "PlatformAdminRolePermissionRepair.cs", UserManage, 1, "administrator repair list"),
        new(Security + "PlatformAdminRolePermissionRepair.cs", RoleAssign, 1, "administrator repair list"),
        new(Security + "PlatformAdminRolePermissionRepair.cs", CredentialManage, 1, "administrator repair list"),
        new(Security + "PlatformAdminRolePermissionRepair.cs", PermissionAssign, 1, "administrator repair list"),
        new(Security + "PlatformAdminRolePermissionRepair.cs", AuthUiPolicyManage, 1, "administrator repair list"),
        new(Security + "PlatformAdminRolePermissionRepair.cs", AuthConfigurationRead, 1, "administrator repair list"),
        new(Security + "PlatformAdminRolePermissionRepair.cs", AuthEntraManage, 1, "administrator repair list"),

        // The floor itself: the ten combinations.
        new(Security + "PlatformAdministratorGovernancePermissions.cs", UserRead, 2, "administrator floor"),
        new(Security + "PlatformAdministratorGovernancePermissions.cs", PermissionAssign, 1, "administrator floor"),
        new(Security + "PlatformAdministratorGovernancePermissions.cs", RoleAssign, 2, "administrator floor"),
        new(Security + "PlatformAdministratorGovernancePermissions.cs", AuthConfigurationRead, 1, "administrator floor"),
        new(Security + "PlatformAdministratorGovernancePermissions.cs", AuthUiPolicyManage, 1, "administrator floor"),
        new(Security + "PlatformAdministratorGovernancePermissions.cs", AuthEntraManage, 1, "administrator floor"),
        new(Security + "PlatformAdministratorGovernancePermissions.cs", UserManage, 1, "administrator floor"),
        new(Security + "PlatformAdministratorGovernancePermissions.cs", CredentialManage, 1, "administrator floor"),

        // The transitional implication.
        new(Security + "EffectivePermissionSet.cs", RoleManage, 1, "role.manage implication"),
    ];

    /// <summary>The helper methods that turn an action and resource type into a check; each must pass scope instance.</summary>
    public static readonly IReadOnlyList<string> InstanceScopeHelpers =
    [
        Security + "Dashboard/DashboardCardProviderAuth.cs",
        Security + "OperatorCapabilitiesQueryService.cs",
    ];

    /// <summary>The distinct permissions the corpus asks for, as the evaluator receives them.</summary>
    public static IEnumerable<PermissionDescriptor> Permissions() => Checks.Select(c => c.ToPermission()).Distinct();
}

/// <summary>One mention of a guarded action constant in a source file.</summary>
internal sealed record ActionMention(string File, string Action, string? ResourceType, string? ScopeMember);

/// <summary>Finds every mention of the guarded action constants in production source.</summary>
internal static partial class AuthorizationSourceScanner
{
    private const string GuardedActions =
        "UserRead|PermissionAssign|RoleAssign|AuthConfigurationRead|AuthUiPolicyManage|AuthEntraManage|UserManage|CredentialManage|RoleManage";

    [GeneratedRegex(@"\bActionConstants\.(?<action>" + GuardedActions + @")\b", RegexOptions.CultureInvariant)]
    private static partial Regex ActionReference();

    [GeneratedRegex(@"ResourceTypeConstants\.(?<type>\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceTypeReference();

    [GeneratedRegex(@"ScopeKeyConstants\.(?<scope>\w+)", RegexOptions.CultureInvariant)]
    private static partial Regex ScopeReference();

    [GeneratedRegex(@"""(user\.read|permission\.assign|role\.assign|auth\.configuration\.read|auth\.ui_policy\.manage|auth\.entra\.manage|user\.manage|credential\.manage|role\.manage)""", RegexOptions.CultureInvariant)]
    private static partial Regex ActionLiteral();

    [GeneratedRegex(@"^\s*using\s+static\s+[\w.]*ActionConstants\s*;", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex UsingStaticActionConstants();

    /// <summary>
    /// Every mention outside comments. The resource type and scope are those of the call the mention belongs to: the first
    /// ones after it, up to the end of the statement, the next mention, or 400 characters.
    /// </summary>
    public static IReadOnlyList<ActionMention> Scan(string file, string text)
    {
        var mentions = new List<ActionMention>();
        var matches = ActionReference().Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (IsInComment(text, match.Index))
                continue;

            var start = match.Index + match.Length;
            var end = Math.Min(text.Length, start + 400);
            if (i + 1 < matches.Count)
                end = Math.Min(end, matches[i + 1].Index);
            var statementEnd = text.IndexOf(';', start, end - start);
            if (statementEnd >= 0)
                end = statementEnd;

            var window = text[start..end];
            var type = ResourceTypeReference().Match(window);
            var scope = ScopeReference().Match(window);
            mentions.Add(new ActionMention(
                file,
                match.Groups["action"].Value,
                type.Success ? type.Groups["type"].Value : null,
                scope.Success ? scope.Groups["scope"].Value : null));
        }

        return mentions;
    }

    /// <summary>Quoted action strings outside comments (a way to ask for a permission the constants would not reveal).</summary>
    public static IReadOnlyList<string> ActionLiterals(string text) =>
        ActionLiteral().Matches(text).Where(m => !IsInComment(text, m.Index)).Select(m => m.Value).ToList();

    public static bool UsesStaticImportOfActionConstants(string text) => UsingStaticActionConstants().IsMatch(text);

    private static bool IsInComment(string text, int index)
    {
        // Only whole-line comments are skipped, so a mention after code on the same line is still counted.
        var lineStart = text.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
        return text[lineStart..index].TrimStart().StartsWith("//", StringComparison.Ordinal);
    }
}
