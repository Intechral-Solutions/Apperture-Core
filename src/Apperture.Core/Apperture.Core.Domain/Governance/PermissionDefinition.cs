using Apperture.Shared.Kernel;

namespace Apperture.Core.Domain.Governance;

/// <summary>
/// A permission in the catalog: its unchanged identity plus the product metadata an operator needs to understand it.
/// </summary>
/// <param name="Identity">The action and resource the permission names. Grants, evaluation and audit use only this.</param>
/// <param name="DisplayName">What an operator sees. Required, at most <see cref="MaxDisplayNameLength"/> characters.</param>
/// <param name="Description">One sentence saying what holding the permission allows. Required, at most <see cref="MaxDescriptionLength"/> characters.</param>
/// <param name="Category">The group the permission is presented under.</param>
/// <param name="Sensitivity">How much harm holding the permission can do.</param>
/// <param name="RequiresStepUp">Whether exercising the permission needs recent strong authentication. Declared here; enforcement is separate.</param>
/// <param name="GrantedToPlatformAdministratorByDefault">Whether the Platform Administrator role is meant to hold the permission without being asked. Catalog metadata: it does not change what any role holds.</param>
/// <param name="Origin">Whether the platform or a module contributed the permission.</param>
/// <param name="ModuleId">The contributing module. Present exactly when <paramref name="Origin"/> is <see cref="PermissionOrigin.Module"/>.</param>
/// <param name="Family">Optional taxonomy grouping.</param>
public sealed record PermissionDefinition(
    PermissionDescriptor Identity,
    string DisplayName,
    string Description,
    PermissionCategory Category,
    PermissionSensitivity Sensitivity,
    bool RequiresStepUp,
    bool GrantedToPlatformAdministratorByDefault,
    PermissionOrigin Origin,
    string? ModuleId = null,
    ResourceFamily? Family = null)
{
    /// <summary>Longest allowed <see cref="DisplayName"/>.</summary>
    public const int MaxDisplayNameLength = 80;

    /// <summary>Longest allowed <see cref="Description"/>.</summary>
    public const int MaxDescriptionLength = 300;
}
