using Apperture.Core.Domain.Governance;
using Apperture.Shared.Kernel;

namespace Apperture.UnitTests.Governance;

/// <summary>Builds complete platform <see cref="PermissionDefinition"/>s for tests that only care about a permission's identity.</summary>
internal static class TestPermissionDefinitions
{
    public static PermissionDefinition For(
        PermissionDescriptor identity,
        ResourceFamily? family = null,
        PermissionSensitivity sensitivity = PermissionSensitivity.Standard) =>
        new(
            identity,
            DisplayName: $"Test {identity.Action}",
            Description: $"Test permission {identity.Action}.",
            Category: PermissionCategory.Platform,
            Sensitivity: sensitivity,
            RequiresStepUp: false,
            GrantedToPlatformAdministratorByDefault: true,
            Origin: PermissionOrigin.Platform,
            ModuleId: null,
            Family: family);

    public static PermissionDefinition For(string action, string resourceType, string scopeKey = "instance", ResourceFamily? family = null) =>
        For(new PermissionDescriptor(action, new ResourceDescriptor(resourceType, scopeKey)), family);
}
