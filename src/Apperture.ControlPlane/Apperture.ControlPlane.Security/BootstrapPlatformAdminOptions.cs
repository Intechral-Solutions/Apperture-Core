namespace Apperture.ControlPlane.Security;

/// <summary>
/// Configuration for assigning the seeded Platform Administrator role to bootstrap principals at host startup.
/// Uses the same identity link model as normal sign-in (Entra: stable oid/sub; Local: normalized username).
/// This is not an authorization bypass: principals receive the same platform administrator role id
/// (<c>role-platform-administrator</c>) and RBAC grants as any other assignment.
/// </summary>
public sealed class BootstrapPlatformAdminOptions
{
    public const string SectionName = "BootstrapPlatformAdmin";

    /// <summary>When false, the hosted assignment step is skipped entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true (default), a user row is created with the identity link and Platform Administrator role if no user matches the principal.
    /// When false, only existing users receive the role assignment.
    /// </summary>
    public bool CreateUserIfMissing { get; set; } = true;

    /// <summary>Principals that must have the Platform Administrator role after startup (idempotent).</summary>
    public List<BootstrapPlatformAdminPrincipalOptions> Principals { get; set; } = new();
}

/// <summary>One bootstrap principal: identity source + external id (oid or local username).</summary>
public sealed class BootstrapPlatformAdminPrincipalOptions
{
    /// <summary><c>Entra</c> or <c>Local</c> (case-insensitive).</summary>
    public string IdentitySource { get; set; } = "";

    /// <summary>Entra: object id (oid) or subject; Local: username (normalized on assignment).</summary>
    public string ExternalId { get; set; } = "";

    /// <summary>Entra: directory tenant id (tid). Required when <see cref="IdentitySource"/> is Entra.</summary>
    public string? TenantId { get; set; }

    /// <summary>Optional display name when <see cref="BootstrapPlatformAdminOptions.CreateUserIfMissing"/> creates a user.</summary>
    public string? DisplayName { get; set; }
}
