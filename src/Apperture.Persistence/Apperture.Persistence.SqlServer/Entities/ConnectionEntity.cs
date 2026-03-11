namespace Apperture.Persistence.SqlServer;

public sealed class ConnectionEntity
{
    public string Id { get; set; } = "";
    public string ConnectorDefinitionId { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigJson { get; set; } = "{}";
    public string SecretRefsJson { get; set; } = "{}";
    public string? ScopeKey { get; set; }
    public string? OwnerId { get; set; }
    public int State { get; set; }
    public byte[]? RowVersion { get; set; }
}
