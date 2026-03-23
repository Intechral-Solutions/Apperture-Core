namespace Apperture.Persistence.SqlServer;

public sealed class EntraInstanceRegistrationEntity
{
    public string Id { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string Authority { get; set; } = "";

    public string ClientId { get; set; } = "";

    public bool UsesDeploymentSecret { get; set; } = true;

    public string? CallbackPath { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
