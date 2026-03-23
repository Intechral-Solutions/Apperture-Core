namespace Apperture.Core.Application.Services;

/// <summary>
/// Persisted Entra (Azure AD) OIDC registration for the instance. At most one row is active at a time.
/// Client secret is not stored; v1 uses deployment configuration when <see cref="UsesDeploymentSecret"/> is true.
/// </summary>
public sealed class EntraInstanceRegistration
{
    public string Id { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string Authority { get; init; } = "";

    public string ClientId { get; init; } = "";

    /// <summary>When true, <c>Entra:ClientSecret</c> (or KeyVault) supplies the confidential client secret.</summary>
    public bool UsesDeploymentSecret { get; init; } = true;

    public string? CallbackPath { get; init; }

    public bool Enabled { get; init; } = true;

    public bool IsActive { get; init; }

    public byte[]? RowVersion { get; init; }

    public DateTimeOffset CreatedUtc { get; init; }

    public DateTimeOffset UpdatedUtc { get; init; }
}
