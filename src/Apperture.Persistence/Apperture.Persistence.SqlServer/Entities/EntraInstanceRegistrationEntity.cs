namespace Apperture.Persistence.SqlServer;

public sealed class EntraInstanceRegistrationEntity
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "";

    public bool UsesDeploymentSecret { get; set; } = true;

    public string? ClientSecretRef { get; set; }

    public string? CallbackPath { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public string? AzureTenantId { get; set; }

    public string? AuthenticationContextId { get; set; }

    public bool AllowMemberAccessRequests { get; set; }

    public bool AllowGuestAccessRequests { get; set; }

    /// <summary>
    /// The registration's probe telemetry (the latest validation and discovery outcomes). Null when no probe has recorded one and
    /// none was created with the row; a reader treats that as the defaults. Never written through this navigation by a probe.
    /// </summary>
    public EntraRegistrationDiagnosticsEntity? Diagnostics { get; set; }
}
