namespace Apperture.Core.Domain.Governance;

/// <summary>Who contributed a permission to the catalog. There is deliberately no zero value, so an unset field is invalid.</summary>
public enum PermissionOrigin
{
    /// <summary>Seeded by the platform.</summary>
    Platform = 1,

    /// <summary>Declared by a module in its manifest.</summary>
    Module = 2,
}
