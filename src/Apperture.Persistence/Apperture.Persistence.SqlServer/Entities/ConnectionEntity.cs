namespace Apperture.Persistence.SqlServer;

public sealed class ConnectionEntity
{
    public string Id { get; set; } = "";

    /// <summary>Logical id of the connector kind; must exist in <see cref="Apperture.Core.Application.Services.IConnectorDefinitionRegistry"/>, not in a SQL definitions table.</summary>
    public string ConnectorDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public string SecretRefsJson { get; set; } = "{}";
    /// <summary>Authorization scope. Unrelated to <see cref="ResourceContextId"/>, which is ownership.</summary>
    public string? ScopeKey { get; set; }

    /// <summary>Owning ResourceContext. Required; existing rows were backfilled to the reserved Shared context.</summary>
    public string ResourceContextId { get; set; } = "";
    public string? OwnerId { get; set; }
    public int State { get; set; }
    public int LastVerificationStatus { get; set; }
    public DateTimeOffset? LastVerificationAtUtc { get; set; }
    public string? LastVerificationSummary { get; set; }
    public byte[]? RowVersion { get; set; }
}
