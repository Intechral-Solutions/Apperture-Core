namespace Apperture.Persistence.SqlServer;

public sealed class ConnectionEntity
{
    public string Id { get; set; } = "";

    /// <summary>Logical id of the connector kind; must exist in <see cref="Apperture.Core.Application.Services.IConnectorDefinitionRegistry"/>, not in a SQL definitions table.</summary>
    public string ConnectorDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public string SecretRefsJson { get; set; } = "{}";
    public string? ScopeKey { get; set; }
    public string? OwnerId { get; set; }
    public int State { get; set; }
    public int LastVerificationStatus { get; set; }
    public DateTimeOffset? LastVerificationAtUtc { get; set; }
    public string? LastVerificationSummary { get; set; }
    public byte[]? RowVersion { get; set; }
}
